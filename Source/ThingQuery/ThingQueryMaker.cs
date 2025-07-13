﻿using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using RimWorld;

namespace TD_Find_Lib
{
	// Create a ThingQuery in code wth ThingQueryMaker.MakeQuery<ThingQueryWhatever>();

	[StaticConstructorOnStartup]
	public static class ThingQueryMaker
	{
		// Construct a ThingQuery subclass, automatically assigning the appropriate Def
		// (This mod doesn't use it but other mods will)
		// The result ThingQuery is to be added to a IQueryHolder with Add()
		// (Probably a QuerySearch or a ThingQueryAndOrGroup)
		private static readonly Dictionary<Type, ThingQueryDef> queryDefForType = new();
		public static ThingQueryDef QueryDefForType(Type t) =>
			queryDefForType[t];

		public static T MakeQuery<T>() where T : ThingQuery =>
			(T)MakeQuery(QueryDefForType(typeof(T)));

		public static ThingQuery MakeQuery(ThingQueryDef def)
		{
			if (Find.WindowStack != null &&
				def.queryClass == typeof(ThingQueryCustom) && !Mod.settings.warnedCustom)
			{
				// If no stack, this is loading, no need to warn

				Mod.settings.warnedCustom = true;
				Mod.settings.Write();

				Find.WindowStack.Add(new Dialog_MessageBox("TD.WarnCustom".Translate()));
			}


			ThingQuery query = (ThingQuery)Activator.CreateInstance(def.queryClass);
			query.def = def;
			return query;
		}

		public static ThingQuery MakeQuery(ThingQueryPreselectDef preDef)
		{
			ThingQuery query = MakeQuery(preDef.queryDef);

			Log.Message($"Making Prequery {preDef}");
			foreach ((string key, string value) in preDef.defaultValues.values)
			{ 
				//todo: store/save these Reflections calls for speed. meh.

				Log.Message($" Setting {key} = {value}");
				// TODO: This change is not confirmed
				// RW1-5, where GetFieldInfoForType was used, in the exact place RW1-6 uses XmlToObjectUtils.DoFieldSearch
				// However, it needs a xmlnode object instead of a name
				// Inside XmlToObjectUtils.DoFieldSearch, it's calling XmlToObjectUtils.DirectGetFieldByName
				// This one has the same signature but less complexity compared to DirectGetFieldByName
				// Need to verify this is actually working correctly
				if (XmlToObjectUtils.DirectGetFieldByName(preDef.queryDef.queryClass, key, null) is FieldInfo field)
				{
					object obj = ConvertHelper.Convert(value, field.FieldType);
					field.SetValue(query, obj);
					continue;
				}

				if (preDef.queryDef.queryClass.GetProperty(key, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty) is PropertyInfo prop)
				{
					object obj = ConvertHelper.Convert(value, prop.PropertyType);
					prop.SetMethod.Invoke(query, new object[] { obj });
					continue;
				}


				Verse.Log.Error($"ThingQueryPreselectDef '{preDef}' Couldn't find how to set {preDef.queryDef.queryClass}.{key}");
			}

			return query;
		}

		// Categories, and Queries that aren't grouped under a Category
		private static readonly List<ThingQuerySelectableDef> rootSelectableQueries;

		// moddedQueiries was gonna be used to smart save them. But that never happened.
		public static readonly List<ThingQueryDef> moddedQueries;

		public static void EnsureStaticInit() { }
		static ThingQueryMaker()
		{
			rootSelectableQueries = DefDatabase<ThingQuerySelectableDef>.AllDefs.ToList();


			// Remove any query that's in a subcategory from the main menu
			foreach (var listDef in DefDatabase<ThingQueryCategoryDef>.AllDefs)
				foreach (ThingQuerySelectableDef subDef in listDef.subQueries)
					if (!subDef.topLevelSelectable)
						rootSelectableQueries.Remove(subDef);


			// Find modded queries
			var basePack = LoadedModManager.GetMod<Mod>().Content;
			List<ThingQuerySelectableDef> moddedSelections = rootSelectableQueries.FindAll(
				def => def.modContentPack != basePack || def.mod != null);


			// Queries that depend on other mods (not expansions that are included here)
			bool FromMod(ThingQuerySelectableDef sDef)
			{
				// if (sDef is not ThingQueryDef qDef) return false;

				// Eventually someone will make a mod that defines a ThingQueryDef
				if (sDef.modContentPack != basePack) return true;

				// Until then, queries that depend on mods are defined with the string mod field
				if(sDef.mod != null)
				{
					// But skip expansions: their code is in vanilla DLL, whether installed or not
					// (They might not load into a game because of missing defs, but at least they can be saved to library)
					return !ModContentPack.ProductPackageIDs.Contains(sDef.mod);
				}

				return false;
			}
			moddedQueries = moddedSelections.Where(FromMod).Cast<ThingQueryDef>().ToList();


			// Remove the mod category if there's no modded filters
			if (moddedSelections.Count == 0)
				rootSelectableQueries.Remove(ThingQueryDefOf.Category_Mod);
			else
			{
				// Move Query_Mod, and all queries from mods, into Category_Mod
				rootSelectableQueries.Remove(ThingQueryDefOf.Query_Mod);
				moddedSelections.ForEach(d => rootSelectableQueries.Remove(d));

				Dictionary<string, List<ThingQuerySelectableDef>> modGroups = new();
				foreach(var def in moddedSelections)
				{
					string packageId = def.mod ?? def.modContentPack.PackageId;
					if(modGroups.TryGetValue(packageId, out var defs))
					{
						defs.Add(def);
					}
					else
					{
						modGroups[packageId] = new List<ThingQuerySelectableDef>() { def };
					}
				}

				List<ThingQuerySelectableDef> modMenu = new() { ThingQueryDefOf.Query_Mod };
				foreach ((string packageId, var defs) in modGroups)
				{
					if (defs.Count == 1)
						modMenu.AddRange(defs);
					else
					{
						ThingQueryCategoryDef catMenuSelectable = new();
						//catMenuSelectable.modContentPack = Other mods potentially, no big deal to omit this
						catMenuSelectable.mod = packageId;
						catMenuSelectable.label = ModLister.AllInstalledMods.FirstOrDefault(mod => mod.PackageId == packageId)?.Name ?? packageId;
						catMenuSelectable.subQueries.AddRange(defs);

						modMenu.Add(catMenuSelectable);

						DefDatabase<ThingQuerySelectableDef>.Add(catMenuSelectable);
						DefDatabase<ThingQueryCategoryDef>.Add(catMenuSelectable);// for good measure.
					}
				}

				ThingQueryDefOf.Category_Mod.subQueries = modMenu;

				// Also insert where requested. The filter can end up in two places (as we already do for things like Stuff)
				foreach (var def in moddedSelections)
				{
					if (def.insertCategory != null)
						def.insertCategory.subQueries.Add(def);

					if (def.insertCategories != null)
						foreach(var cat in def.insertCategories)
							cat.subQueries.Add(def);
				}
			}


			// Construct the Def=>Query class dictionary so we can create Queries from MakeQuery<T> above
			foreach (var queryDef in DefDatabase<ThingQueryDef>.AllDefsListForReading)
				queryDefForType[queryDef.queryClass] = queryDef;

			// Make dummy defs for All ThingQueryCategorizedDropdownHelper
			// so they don't trigger warning below, but aren't included as selectable
			var modContentPack = LoadedModManager.GetMod<Mod>().Content;
			
			// GenTypes.AllSubclassesNonAbstract doesnt check generics properly so:
			foreach (Type helperType in (from x in GenTypes.AllTypes
																	 where !x.IsAbstract && x.IsSubclassOfRawGeneric(typeof(ThingQueryCategorizedDropdownHelper<,,,>))
																	 select x).ToList())
			{
				ThingQueryDef dummyDef = new();
				dummyDef.defName = "ThingQueryHelper_" + helperType.Name;
				dummyDef.queryClass = helperType;
				dummyDef.modContentPack = modContentPack;

				queryDefForType[helperType] = dummyDef;

				DefGenerator.AddImpliedDef(dummyDef);
			}


			// Config Error check
			foreach (var queryType in GenTypes.AllSubclassesNonAbstract(typeof(ThingQuery)))
				if (!queryDefForType.ContainsKey(queryType))
					Verse.Log.Error($"TDFindLib here, uhhh, there is no ThingQueryDef for {queryType}, thought you should know.");
		}

		public static IEnumerable<ThingQuerySelectableDef> RootQueries => rootSelectableQueries;


		// am I dabblin so far into the arcane I need this helper?
		static bool IsSubclassOfRawGeneric(this Type toCheck, Type generic)
		{
			while (toCheck != null && toCheck != typeof(object))
			{
				var cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
				if (generic == cur)
				{
					return true;
				}
				toCheck = toCheck.BaseType;
			}
			return false;
		}
	}

	[DefOf]
	public static class ThingQueryDefOf
	{
		public static ThingQueryCategoryDef Category_Mod;
		public static ThingQueryDef Query_Mod;
		public static ThingQueryDef Query_Ability;
	}
}
