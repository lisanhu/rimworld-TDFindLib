﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using RimWorld;
using UnityEngine;

namespace TD_Find_Lib
{
	public class ThingQueryName : ThingQueryWithOption<string>
	{
		public ThingQueryName() => sel = "";

		public override bool AppliesDirectlyTo(Thing thing) =>
			//thing.Label.Contains(sel, CaseInsensitiveComparer.DefaultInvariant);	//Contains doesn't accept comparer with strings. okay.
			sel == "" || thing.Label.IndexOf(sel, StringComparison.OrdinalIgnoreCase) >= 0;

		protected override string MakeLabel() => "TD.Named".Translate();
		protected override bool DrawMain(Rect rect, bool locked, Rect fullRect)
		{
			base.DrawMain(rect, locked, fullRect);
			rect.xMin += row.FinalX;

			if (locked)
			{
				Widgets.Label(rect, '"' + sel + '"');
				return false;
			}

			if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Tab
				&& GUI.GetNameOfFocusedControl() == $"THING_QUERY_NAME_INPUT{id}")
					Event.current.Use();

			Rect resetRect = rect.RightPartPixels(rect.height);
			rect.width -= resetRect.width + WidgetRow.DefaultGap;

			GUI.SetNextControlName($"THING_QUERY_NAME_INPUT{id}");
			string newStr = Widgets.TextField(rect, sel);
			if (newStr != sel)
			{
				sel = newStr;
				return true;
			}
			if (Widgets.ButtonImage(resetRect, TexUI.RotLeftTex))
			{
				UI.UnfocusCurrentControl();
				sel = "";
				return true;
			}
			return false;
		}

		protected override void DoFocus()
		{
			GUI.FocusControl($"THING_QUERY_NAME_INPUT{id}");
		}
		public override bool Unfocus()
		{
			if (GUI.GetNameOfFocusedControl() == $"THING_QUERY_NAME_INPUT{id}")
			{
				UI.UnfocusCurrentControl();
				return true;
			}

			return false;
		}
	}

	public enum ForbiddenType { Forbidden, Allowed, Forbiddable }
	public class ThingQueryForbidden : ThingQueryDropDown<ForbiddenType>
	{
		public override bool AppliesDirectlyTo(Thing thing)
		{
			bool forbiddable = thing.def.HasComp(typeof(CompForbiddable)) && thing.Spawned;
			if (!forbiddable) return false;
			bool forbidden = thing.IsForbidden(Faction.OfPlayer);
			switch (sel)
			{
				case ForbiddenType.Forbidden: return forbidden;
				case ForbiddenType.Allowed: return !forbidden;
			}
			return true;  //forbiddable
		}
	}

	//ThingQueryThingDef helper class for category
	public class ThingQueryAccessibleCategory : ThingQueryCategorizedDropdownHelper<ThingDef, ThingCategoryDef, ThingQueryAccessible, ThingQueryAccessibleCategory>
	{

		public override bool AppliesDirectlyTo(Thing thing) =>
			ThingQueryAccessible.LeavingsFor(thing).Any(leftDef => CategoryFor(leftDef) == sel);


		public static ThingCategoryDef CategoryFor(ThingDef leftDef)
		{
			// Textiles
			if (ThingCategoryDefOf.Textiles.ContainedInThisOrDescendant(leftDef))
				return ThingCategoryDefOf.Textiles;

			// Raw Resources
			if (ThingCategoryDefOf.ResourcesRaw.ContainedInThisOrDescendant(leftDef))
				return ThingCategoryDefOf.ResourcesRaw;
			
			// Manufactured
			if (ThingCategoryDefOf.Manufactured.ContainedInThisOrDescendant(leftDef))
				return ThingCategoryDefOf.Manufactured;

			// Body Parts
			if (ThingCategoryDefOf.BodyParts.ContainedInThisOrDescendant(leftDef))
				return ThingCategoryDefOf.BodyParts;

			// (Other)
			return null;
		}

		public override string NullOption() => "TD.OtherCategory".Translate();
	}

	public class ThingQueryAccessible : ThingQueryCategorizedDropdown<ThingDef, ThingCategoryDef, ThingQueryAccessible, ThingQueryAccessibleCategory>
	{
		public ThingQueryAccessible() => sel = ThingDefOf.Steel;

		public override ThingCategoryDef CategoryFor(ThingDef def) =>
			ThingQueryAccessibleCategory.CategoryFor(def);

		public override bool AppliesDirectly2(Thing thing) =>
			extraOption == 1 ? LeavingsFor(thing).Any() : 
			LeavingsFor(thing).Any(def => def == sel);


		// Hey this is basically CacheAccessibleThings
		// Plus MedicalRecipesUtility.SpawnThingsFromHediffs
		private static List<Hediff_AddedPart> _hediffAdded = new();
		public static IEnumerable<ThingDef> LeavingsFor(Thing thing)
		{ 
			// Surgery removable (don't bother with harvest as those are expected to stay)
			if(thing is Pawn pawn)
			{
				pawn.health.hediffSet.GetHediffs(ref _hediffAdded, h => h.def.spawnThingOnRemoved is ThingDef);

				foreach (Hediff_AddedPart hediff in _hediffAdded)
			// presumably there's a recipe to access this spawnThingOnRemoved 		{
					yield return hediff.def.spawnThingOnRemoved;
			}


			foreach (var leftDef in LeavingsFor(thing.def, thing.Stuff))
				yield return leftDef;
		}
		public static IEnumerable<ThingDef> LeavingsFor(ThingDef def, ThingDef stuff = null)
		{
			/*
			// Filth created, no one cares.
			if (def.filthLeaving != null)
				yield return def.filthLeaving;
			*/

			// Smelted/Deconstructed/Killed
			bool smeltable = (stuff == null ? def.PotentiallySmeltable : (def.smeltable && stuff.smeltable));
			if (smeltable || 
				(def.building != null && def.building.deconstructible && def.resourcesFractionWhenDeconstructed > 0) ||
				def.leaveResourcesWhenKilled)
			{
				if (def.CostList != null)
					foreach (var d in def.CostList)
						if (!d.thingDef.intricate)	//Clever
							yield return d.thingDef;

				if (stuff == null)
					foreach (var d in GenStuff.AllowedStuffsFor(def))	// Eh, todo: not unsmeltable if only smeltable?
						yield return d;
				else
					yield return stuff;

				// Smelted extra products ffs is this only ChunkSlagSteel
				if (smeltable && def.smeltProducts != null)
					foreach (var d in def.smeltProducts)
						yield return d.thingDef;
			}

			
			// Butchered. Actually skip this for stone blocks.
			if (def.butcherProducts != null && (def.thingCategories == null || !def.thingCategories.Contains( ThingCategoryDefOf.StoneChunks)))
				foreach (var d in def.butcherProducts)
					yield return d.thingDef;
			

			/*
			 * Use Meat/Leather filters
			if (def.race != null)
			{
				if(def.race.meatDef != null)
					yield return def.race.meatDef;
				if(def.race.leatherDef != null)
				yield return def.race.leatherDef;
			}
			*/

			/*
			 * Use the minable filter instead.
			 * A mineable object's only purpose is to be mined, 
			 * whereas this filter is for "accessible" but not exactly "inevitable" things
			// Mined
			if (def.building?.mineableThing is ThingDef productDef)
				yield return productDef;
			*/

			/*
			 * Use the plant harvest filter
			// Harvested
			if (def.plant?.harvestedThingDef is ThingDef harvestDef)
				yield return harvestDef;
			*/


			// left when killed
			if (def.killedLeavings != null)
				foreach (var d in def.killedLeavings)
					yield return d.thingDef;

			if (def.killedLeavingsPlayerHostile != null)
				foreach (var d in def.killedLeavingsPlayerHostile)
					yield return d.thingDef;
		}


		public static readonly List<ThingDef> leavingDefs;

		static ThingQueryAccessible()
		{
			leavingDefs =
			DefDatabase<ThingDef>.AllDefsListForReading
			.SelectMany(def => LeavingsFor(def)).ToHashSet().ToList();

			leavingDefs.AddRange(
				DefDatabase<HediffDef>.AllDefsListForReading
				.Where(def => def.hediffClass == typeof(Hediff_AddedPart) && def.spawnThingOnRemoved is ThingDef)
				.Select(def =>  def.spawnThingOnRemoved));
		}
		public override IEnumerable<ThingDef> AllOptions() => leavingDefs;
		public override IEnumerable<ThingDef> AvailableOptions() =>
			ContentsUtility.AvailableInGame(LeavingsFor);

		public override int ExtraOptionsCount => 1;
		public override string NameForExtra(int ex) => "TD.AnyOption".Translate();

		public override ThingDef IconDefFor(ThingDef o) => o;//duh
	}



	public class ThingQueryDesignation : ThingQueryDropDown<DesignationDef>
	{
		public ThingQueryDesignation() => extraOption = 1;

		public override bool AppliesDirectlyTo(Thing thing)
		{
			if (extraOption == 1)
				return thing.MapHeld.designationManager.DesignationOn(thing) != null
					|| thing.MapHeld.designationManager.AllDesignationsAt(thing.PositionHeld).Count() > 0;

			if (sel == null)
				return thing.MapHeld.designationManager.DesignationOn(thing) == null
					&& thing.MapHeld.designationManager.AllDesignationsAt(thing.PositionHeld).Count() == 0;

			return sel.targetType == TargetType.Thing ? thing.MapHeld.designationManager.DesignationOn(thing, sel) != null :
				thing.MapHeld.designationManager.DesignationAt(thing.PositionHeld, sel) != null;
		}

		public override string NullOption() => "None".Translate();

		public override int ExtraOptionsCount => 1;
		public override string NameForExtra(int ex) => "TD.AnyOption".Translate();

		public override bool Ordered => true;
		//There really aren't too many designations to filter them down
		//public override IEnumerable<DesignationDef> AvailableOptions() =>
		//	Find.CurrentMap.designationManager.AllDesignations.Select(d => d.def);
	}

	public class ThingQueryFreshness : ThingQueryDropDown<RotStage>
	{
		public override bool AppliesDirectlyTo(Thing thing)
		{
			CompRottable rot = thing.TryGetComp<CompRottable>();
			return
				extraOption == 1 ? rot != null :
				extraOption == 2 ? GenTemperature.RotRateAtTemperature(thing.AmbientTemperature) is float r && r > 0 && r < 1 :
				extraOption == 3 ? GenTemperature.RotRateAtTemperature(thing.AmbientTemperature) <= 0 :
				rot?.Stage == sel;
		}

		public override string NameFor(RotStage o) => ("RotState" + o.ToString()).Translate();

		public override int ExtraOptionsCount => 3;
		public override string NameForExtra(int ex) =>
			ex == 1 ? "TD.Spoils".Translate() :
			ex == 2 ? "TD.Refrigerated".Translate() :
			"TD.Frozen".Translate();
	}

	public class ThingQueryTimeToRot : ThingQueryIntRange
	{
		public override int Max => GenDate.TicksPerDay * 20;
		public override Func<int, string> Writer => ticks => $"{ticks * 1f / GenDate.TicksPerDay:0.0}";

		public ThingQueryTimeToRot() => _sel.max = Max / 2;

		public override bool AppliesDirectlyTo(Thing thing) =>
			thing.TryGetComp<CompRottable>()?.TicksUntilRotAtCurrentTemp is int t && sel.Includes(t);
	}

	public class ThingQueryGrowth : ThingQueryFloatRange
	{
		public override bool AppliesDirectlyTo(Thing thing) =>
			thing is Plant p && sel.Includes(p.Growth);
	}

	public class ThingQueryGrowthRate : ThingQueryFloatRange
	{
		public override bool AppliesDirectlyTo(Thing thing) =>
			thing is Plant p && sel.Includes(p.GrowthRate);

		public override float Max => maxGrowthRate;
		public static float maxGrowthRate;
		static ThingQueryGrowthRate()
		{
			float bestFertility = 0f;
			foreach (BuildableDef def in DefDatabase<BuildableDef>.AllDefs)
				bestFertility = Mathf.Max(bestFertility, def.fertility);

			float bestSensitivity = 0f;
			foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
				bestSensitivity = Mathf.Max(bestSensitivity, def.plant?.fertilitySensitivity ?? 0);

			maxGrowthRate = 1 + bestSensitivity * (bestFertility - 1);
		}
	}

	public class ThingQueryPlantHarvest : ThingQueryDropDown<ThingDef>
	{
		public ThingQueryPlantHarvest() => extraOption = 1;

		public override bool AppliesDirectlyTo(Thing thing)
		{
			Plant plant = thing as Plant;
			if (plant == null)
				return false;

			ThingDef yield = plant.def.plant.harvestedThingDef;

			if (extraOption == 1)
				return yield != null;
			if (sel == null)
				return yield == null;

			return sel == yield;
		}

		public static List<ThingDef> allHarvests;
		static ThingQueryPlantHarvest()
		{
			HashSet<ThingDef> singleDefs = new();
			foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
			{
				if (def.plant?.harvestedThingDef is ThingDef harvestDef)
					singleDefs.Add(harvestDef);
			}
			allHarvests = singleDefs.OrderBy(d => d.label).ToList();
		}

		public override IEnumerable<ThingDef> AllOptions() => allHarvests;
		public override IEnumerable<ThingDef> AvailableOptions() =>
			ContentsUtility.AvailableInGame(t => (t as Plant)?.def.plant.harvestedThingDef);
			/* This is marginally better but inconsistent and a bunch of repeated code
			 * just to save time using ThingRequestGroup.HarvestablePlant instead of all items
		{
			HashSet<ThingDef> available = new HashSet<ThingDef>();
			foreach (Map map in Find.Maps)
				foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.HarvestablePlant))
					if ((t as Plant)?.def.plant.harvestedThingDef is ThingDef harvestDef)
						available.Add(harvestDef);

			return available;
		}
			*/

		public override bool Ordered => true;
		public override ThingDef IconDefFor(ThingDef def) => def;//duh;

		public override string NullOption() => "None".Translate();

		public override int ExtraOptionsCount => 1;
		public override string NameForExtra(int ex) => "TD.AnyOption".Translate();
	}


	public class ThingQueryPlantHarvestable : ThingQuery
	{
		public override bool AppliesDirectlyTo(Thing thing) =>
			thing is Plant plant && plant.HarvestableNow;
	}

	public class ThingQueryPlantCrop : ThingQuery
	{
		public override bool AppliesDirectlyTo(Thing thing) =>
			thing is Plant plant && plant.IsCrop;
	}

	public class ThingQueryPlantDies : ThingQuery
	{
		public override bool AppliesDirectlyTo(Thing thing) =>
			thing is Plant plant && (plant.def.plant?.dieIfLeafless ?? false);
	}

	public class ThingQueryPlantBlighted : ThingQuery
	{
		public override bool AppliesDirectlyTo(Thing thing) =>
			thing is Plant plant && plant.Blighted;
	}

	[DefOf]
	public static class GoodwillSituationDefOf
	{
		public static GoodwillSituationDef PermanentEnemy;
	}
	public class ThingQueryFaction : ThingQueryDropDown<FactionRelationKind>
	{
		public bool host; // compare host faction instead of thing's faction
		public ThingQueryFaction() => extraOption = 1;

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref host, "host");
		}
		protected override ThingQuery Clone()
		{
			ThingQueryFaction clone = (ThingQueryFaction)base.Clone();
			clone.host = host;
			return clone;
		}

		public override bool AppliesDirectlyTo(Thing thing)
		{
			Faction fac = thing.Faction;
			if (host)
			{
				if (thing is Pawn p && p.guest != null)
					fac = p.guest.HostFaction;
				else
					return false;
			}

			return
				extraOption == 1 ? fac == Faction.OfPlayer :
				extraOption == 2 ? fac == Faction.OfMechanoids :
				extraOption == 3 ? fac == Faction.OfInsects :
				extraOption == 4 ? fac != null && !fac.def.hidden :
				extraOption == 5 ? fac == null || fac.def.hidden :
				extraOption == 6 ? fac != null && fac.def.permanentEnemy :
				(fac != null && fac != Faction.OfPlayer && fac.PlayerRelationKind == sel);
		}

		public override string NameFor(FactionRelationKind o) => o.GetLabel();

		public override int ExtraOptionsCount => 6;
		public override string NameForExtra(int ex) =>
			ex == 1 ? "TD.Player".Translate() :
			ex == 2 ? "TD.Mechanoid".Translate() :
			ex == 3 ? "TD.Insectoid".Translate() :
			ex == 4 ? "TD.AnyOption".Translate() :
			ex == 5 ? "TD.NoFaction".Translate() :
				GoodwillSituationDefOf.PermanentEnemy.LabelCap;

		protected override bool DrawMain(Rect rect, bool locked, Rect fullRect)
		{
			//This is not DrawCustom because then the faction button would go on the left.
			bool changed = base.DrawMain(rect, locked, fullRect);

			Rect hostRect = fullRect.LeftPart(0.6f);
			hostRect.xMin = hostRect.xMax - 60;
			if (Widgets.ButtonText(hostRect, host ? "TD.HostIs".Translate() : "TD.FactionIs".Translate()))
			{
				host = !host;
				changed = true;
			}

			return changed;
		}

	}

	public enum ListCategory
	{
		Person,
		Animal,
		Item,
		Building,
		Natural,
		Plant,
		Other
	}
	public class ThingQueryCategory : ThingQueryDropDown<ListCategory>
	{
		public override bool AppliesDirectlyTo(Thing thing) =>
			sel switch
			{	
				ListCategory.Person => thing is Pawn person && !person.AnimalOrWildMan(),
				ListCategory.Animal => thing is Pawn animal && animal.AnimalOrWildMan(),
				ListCategory.Item => thing.def.alwaysHaulable,
				ListCategory.Building => thing is Building building && building.def.filthLeaving != ThingDefOf.Filth_RubbleRock,
				ListCategory.Natural => thing is Building natural && natural.def.filthLeaving == ThingDefOf.Filth_RubbleRock,
				ListCategory.Plant => thing is Plant,
				ListCategory.Other =>
					!(thing is Pawn) && !(thing is Building) && !(thing is Plant) && !thing.def.alwaysHaulable,
				_ => false
			};
	}

	// This includes most things but not minifiable buildings.
	public class ThingQueryItemCategory : ThingQueryDropDown<ThingCategoryDef>
	{
		public ThingQueryItemCategory() => sel = ThingCategoryDefOf.Root;

		public override bool AppliesDirectlyTo(Thing thing) =>
			thing.def.IsWithinCategory(sel);

		public override IEnumerable<ThingCategoryDef> AvailableOptions() =>
			ContentsUtility.AvailableInGame(ThingCategoryDefsOfThing);

		public static IEnumerable<ThingCategoryDef> ThingCategoryDefsOfThing(Thing thing)
		{
			if (thing.def.thingCategories == null)
				yield break;
			foreach (var def in thing.def.thingCategories)
			{
				yield return def;
				foreach (var pDef in def.Parents)
					yield return pDef;
			}
		}

		public override string DropdownNameFor(ThingCategoryDef def) =>
			string.Concat(Enumerable.Repeat("- ", def.Parents.Count())) + base.NameFor(def);

		//public override Texture2D IconTexFor(ThingCategoryDef def) => def.icon; //They don't all have icons so no
	}

	public class ThingQuerySpecialFilter : ThingQueryDropDown<SpecialThingFilterDef>
	{
		public ThingQuerySpecialFilter() => sel = SpecialThingFilterDefOf.AllowFresh;

		public override bool AppliesDirectlyTo(Thing thing) =>
			sel.Worker.Matches(thing);
	}

	public class ThingQueryApparelLayer : ThingQueryMask<ApparelLayerDef>
	{
		public ThingQueryApparelLayer()
		{
			mask.AddMust(ApparelLayerDefOf.OnSkin);
			mask.SetLabel();
		}

		public static List<ApparelLayerDef> layers = DefDatabase<ApparelLayerDef>.AllDefsListForReading;
		public override List<ApparelLayerDef> Options => layers;

		public override int CompareSelector(ApparelLayerDef def) => def.drawOrder;


		public override bool AppliesDirectlyTo(Thing thing)
		{
			Apparel apparel = thing as Apparel;
			if (apparel == null) return false;

			var layers = apparel.def.apparel?.layers;
			if (layers == null) return false;

			return mask.AppliesTo(layers);
		}
	}

	public class ThingQueryApparelCoverage : ThingQueryMask<BodyPartGroupDef>
	{
		public ThingQueryApparelCoverage()
		{
			mask.AddMust(BodyPartGroupDefOf.Legs);
			mask.SetLabel();
		}

		public static List<BodyPartGroupDef> partGroups;
		public override List<BodyPartGroupDef> Options => partGroups;
		static ThingQueryApparelCoverage()
		{
			HashSet<BodyPartGroupDef> apparelCoveredParts = new();
			foreach(ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
			{
				if (def.apparel?.bodyPartGroups is List<BodyPartGroupDef> parts)
					apparelCoveredParts.AddRange(parts);
			}

			partGroups = apparelCoveredParts.ToList();

			partGroups.SortBy(d => d.listOrder);
		}

		public override int CompareSelector(BodyPartGroupDef def) => -def.listOrder;


		public override bool AppliesDirectlyTo(Thing thing)
		{
			Apparel apparel = thing as Apparel;
			if (apparel == null) return false;

			var groups = apparel.def.apparel?.bodyPartGroups;
			if (groups == null) return false;

			return mask.AppliesTo(groups);
		}
	}


	// obsolete
	public enum MineableType { Resource, Rock, All }
	public class ThingQueryMineable : ThingQueryDropDown<MineableType>
	{
		public override bool AppliesDirectlyTo(Thing thing)
		{
			switch (sel)
			{
				case MineableType.Resource: return thing.def.building?.isResourceRock ?? false;
				case MineableType.Rock: return (thing.def.building?.isNaturalRock ?? false) && (!thing.def.building?.isResourceRock ?? true);
				case MineableType.All: return thing.def.mineable;
			}
			return false;
		}
	}

	public class ThingQueryMineableDef : ThingQueryDropDown<ThingDef>
	{
		public ThingQueryMineableDef() => extraOption = 1;

		public override bool AppliesDirectlyTo(Thing thing) =>
			extraOption switch
			{
				1 => thing.def.building?.isResourceRock ?? false,
				2 => (thing.def.building?.isNaturalRock ?? false) && (!thing.def.building?.isResourceRock ?? true),
				3 => thing.def.mineable,
				_ => thing.def.building?.mineableThing == sel
			};

		public static readonly List<ThingDef> options = DefDatabase<ThingDef>.AllDefsListForReading
			.Select(def => def.building?.mineableThing).Distinct().Where(d => d != null).ToList();
		public override IEnumerable<ThingDef> AllOptions() => options;

		public override ThingDef IconDefFor(ThingDef o) => o;//duh

		public override int ExtraOptionsCount => 3;
		public override string NameForExtra(int ex)
		{
			string key = ex switch { 1 => "TD.Resource", 2 => "TD.Rock", _ => "TD.Mineable" }; // not .AnyOption because technically mienable doesn't mean resources?
			return key.Translate();
		}
	}

	public class ThingQueryHP : ThingQueryFloatRange
	{
		public override bool AppliesDirectlyTo(Thing thing)
		{
			if (thing is Pawn pawn)
				return sel.Includes(pawn.health.summaryHealth.SummaryHealthPercent);

			if (thing.def.useHitPoints)
				return sel.Includes((float)thing.HitPoints / thing.MaxHitPoints);

			return false;
		}
	}

	public class ThingQueryQuality : ThingQueryWithOption<QualityRange>
	{
		public ThingQueryQuality() => sel = QualityRange.All;

		public override bool AppliesDirectlyTo(Thing thing) =>
			thing.TryGetQuality(out QualityCategory qc) &&
			sel.Includes(qc);

		protected override bool DrawMain(Rect rect, bool locked, Rect fullRect)
		{
			base.DrawMain(rect, locked, fullRect);

			QualityRange newRange = sel;
			Widgets.QualityRange(fullRect.RightHalfClamped(row.FinalX), id, ref newRange);
			if (sel != newRange)
			{
				sel = newRange;
				return true;
			}
			return false;
		}
	}

	public class ThingQueryStuff : ThingQueryDropDown<ThingDef>
	{
		public ThingQueryStuff() => sel = ThingDefOf.Steel;

		public override bool AppliesDirectlyTo(Thing thing)
		{
			ThingDef stuff = thing is IConstructible c ? c.EntityToBuildStuff() : thing.Stuff;
			return
				extraOption == 1 ? stuff != null :
				extraOption > 1 ? stuff?.stuffProps?.categories?.Contains(DefDatabase<StuffCategoryDef>.AllDefsListForReading[extraOption - 2]) ?? false :
				sel == null ? !thing.def.MadeFromStuff :
				stuff == sel;
		}

		public override string NullOption() => "TD.NotMadeFromStuff".Translate();

		private static List<ThingDef> stuffList = DefDatabase<ThingDef>.AllDefs.Where(d => d.IsStuff).ToList();

		public override IEnumerable<ThingDef> AllOptions() => stuffList;
		public override IEnumerable<ThingDef> AvailableOptions() =>
			ContentsUtility.AvailableInGame(t => t.Stuff);

		public override int ExtraOptionsCount => DefDatabase<StuffCategoryDef>.DefCount + 1;
		public override string NameForExtra(int ex) =>
			ex == 1 ? "TD.AnyOption".Translate():
			DefDatabase<StuffCategoryDef>.AllDefsListForReading[ex - 2]?.LabelCap;

		public override ThingDef IconDefFor(ThingDef def) => def;//duh
	}

	public enum AnyAllOrNone { Any, All, None }
	public class ThingQueryMissingBodyPart : ThingQueryDropDown<BodyPartDef>
	{
		//Parts with multiple copies in a body
		public static HashSet<BodyPartDef> multiParts;
		static ThingQueryMissingBodyPart()
		{
			multiParts = new();
			foreach(BodyPartDef part in DefDatabase<BodyPartDef>.AllDefsListForReading)
			{
				foreach(BodyDef body in DefDatabase<BodyDef>.AllDefsListForReading)
				{
					if (body.GetPartsWithDef(part).Count > 1)
					{
						multiParts.Add(part);
						break;
					}
				}
			}
		}



		public AnyAllOrNone filterType;
		public override bool AppliesDirectlyTo(Thing thing)
		{
			Pawn pawn = thing as Pawn;
			if (pawn == null) return false;

			if (extraOption == 1)
				return !pawn.health.hediffSet.GetMissingPartsCommonAncestors().NullOrEmpty();

			if (sel == null)
				return pawn.health.hediffSet.GetMissingPartsCommonAncestors().NullOrEmpty();

			var parts = pawn.RaceProps.body.GetPartsWithDef(sel);

			if (parts.Count == 0) return false; //skip those without this part

			return filterType switch
			{
				AnyAllOrNone.Any => parts.Any(r => pawn.health.hediffSet.PartIsMissing(r)),
				AnyAllOrNone.All => parts.All(r => pawn.health.hediffSet.PartIsMissing(r)),
				_ /*None*/       => !parts.Any(r => pawn.health.hediffSet.PartIsMissing(r))
			};
		}

		public override void ExposeData()
		{
			base.ExposeData();

			Scribe_Values.Look(ref filterType, "filterType");
		}
		protected override ThingQuery Clone()
		{
			ThingQueryMissingBodyPart clone = (ThingQueryMissingBodyPart)base.Clone();
			clone.filterType = filterType;
			return clone;
		}

		protected override void PostChosen()
		{
			if(multiParts!.Contains(sel))
				filterType = default;
		}


		public override string NullOption() => "None".Translate();
		public override IEnumerable<BodyPartDef> AvailableOptions() =>
			ContentsUtility.AvailableInGame(
					t => (t as Pawn)?.health.hediffSet.GetMissingPartsCommonAncestors().Select(h => h.Part.def));

		public override string NameFor(BodyPartDef def)
		{
			string name = def.GetLabel();
			string special = def.defName.SplitCamelCase(); //best we got
			if (name == special)
				return name;

			return $"{name} ({special})";
		}

		public override int ExtraOptionsCount => 1;
		public override string NameForExtra(int ex) => "TD.AnyOption".Translate();


		protected override bool DrawMain(Rect rect, bool locked, Rect fullRect)
		{
			//This is not DrawCustom because then the selection button would go on the left.
			bool changed = base.DrawMain(rect, locked, fullRect);

			if (extraOption == 0 && sel != null)
			{
				if(multiParts.Contains(sel))
					changed |= row.ButtonCycleEnum(ref filterType);
			}

			return changed;
		}
	}


	public enum BaseAreas { Home, BuildRoof, NoRoof, SnowClear };
	public class ThingQueryArea : ThingQueryDropDown<Area>
	{
		public ThingQueryArea() => extraOption = 1;

		protected override Area ResolveRef(Map map) =>
			map.areaManager.GetLabeled(selName);

		public override bool AppliesDirectlyTo(Thing thing)
		{
			Map map = thing.MapHeld;
			IntVec3 pos = thing.PositionHeld;

			if (extraOption == 5)
				return pos.Roofed(map);

			if (extraOption == 6)
				return map.areaManager.AllAreas.Any(a => a[pos]);

			if (extraOption == 0)
				return sel[pos];

			switch ((BaseAreas)(extraOption - 1))
			{
				case BaseAreas.Home: return map.areaManager.Home[pos];
				case BaseAreas.BuildRoof: return map.areaManager.BuildRoof[pos];
				case BaseAreas.NoRoof: return map.areaManager.NoRoof[pos];
				// TODO, snow clear area is changed to snow or sand clear in RW1.6
				// This is a temporary fix until further changes are made.
				case BaseAreas.SnowClear: return map.areaManager.SnowOrSandClear[pos];
			}
			return false;
		}

		public override IEnumerable<Area> AllOptions() =>
			Find.CurrentMap?.areaManager.AllAreas.Where(a => a is Area_Allowed);

		public override string NameFor(Area o) => o.Label;

		public override int ExtraOptionsCount => 5;
		public override string NameForExtra(int ex)
		{
			if (ex == 5) return "Roofed".Translate().CapitalizeFirst();
			if (ex == 6) return "TD.AnyOption".Translate();
			switch ((BaseAreas)(ex - 1))
			{
				case BaseAreas.Home: return "Home".Translate();
				case BaseAreas.BuildRoof: return "BuildRoof".Translate().CapitalizeFirst();
				case BaseAreas.NoRoof: return "NoRoof".Translate().CapitalizeFirst();
				case BaseAreas.SnowClear: return "SnowClear".Translate().CapitalizeFirst();
			}
			return "???";
		}
	}

	public class ThingQueryZone : ThingQueryDropDown<Zone>
	{
		public ThingQueryZone() => extraOption = 3;

		protected override Zone ResolveRef(Map map) =>
			map.zoneManager.AllZones.FirstOrDefault(z => z.label == selName);

		public override bool AppliesDirectlyTo(Thing thing)
		{
			IntVec3 pos = thing.PositionHeld;
			Zone zoneAtPos = thing.MapHeld.zoneManager.ZoneAt(pos);
			return
				extraOption == 1 ? zoneAtPos is Zone_Stockpile :
				extraOption == 2 ? zoneAtPos is Zone_Growing :
				extraOption == 3 ? zoneAtPos != null :
				zoneAtPos == sel;
		}

		public override IEnumerable<Zone> AllOptions() =>
			Find.CurrentMap?.zoneManager.AllZones;

		public override int ExtraOptionsCount => 2;
		public override string NameForExtra(int ex) =>
			ex switch
			{
				1 => "TD.AnyStockpile".Translate(),
				2 => "TD.AnyGrowingZone".Translate(),
				_ => "TD.AnyOption".Translate()
			};
	}

	public class ThingQueryDeterioration : ThingQuery
	{
		public override bool AppliesDirectlyTo(Thing thing) =>
			SteadyEnvironmentEffects.FinalDeteriorationRate(thing) >= 0.001f;
	}

	public enum DoorOpenQuery { Open, Close, HoldOpen, BlockedOpenMomentary }
	public class ThingQueryDoorOpen : ThingQueryDropDown<DoorOpenQuery>
	{
		public override bool AppliesDirectlyTo(Thing thing)
		{
			Building_Door door = thing as Building_Door;
			if (door == null) return false;
			switch (sel)
			{
				case DoorOpenQuery.Open: return door.Open;
				case DoorOpenQuery.Close: return !door.Open;
				case DoorOpenQuery.HoldOpen: return door.HoldOpen;
				case DoorOpenQuery.BlockedOpenMomentary: return door.BlockedOpenMomentary;
			}
			return false;//???
		}
		public override string NameFor(DoorOpenQuery o)
		{
			switch (o)
			{
				case DoorOpenQuery.Open: return "TD.Opened".Translate();
				case DoorOpenQuery.Close: return "VentClosed".Translate();
				case DoorOpenQuery.HoldOpen: return "CommandToggleDoorHoldOpen".Translate().CapitalizeFirst();
				case DoorOpenQuery.BlockedOpenMomentary: return "TD.BlockedOpen".Translate();
			}
			return "???";
		}
	}

	
	//ThingQueryThingDef helper class for category
	public class ThingQueryThingDefCategory : ThingQueryCategorizedDropdownHelper<ThingDef, string, ThingQueryThingDef, ThingQueryThingDefCategory>
	{
		public override bool AppliesDirectlyTo(Thing thing) =>
			CategoryFor(thing.def) == sel;


		public static string CategoryFor(ThingDef def)
		{
			//Blueprints etc
			if (typeof(Blueprint_Install).IsAssignableFrom(def.thingClass))
				return "TD.InstallingCategory".Translate();

			string blueprintKey = null; // Might append "Terrain"
			if (def.IsFrame)  //Terrain blueprints are not ethereal so you gotta check frame first?
				blueprintKey = "TD.FrameCategory";
			else if (def.IsBlueprint)
				blueprintKey = "TD.BlueprintCategory";

			if (blueprintKey != null)
			{
				// Terrain is not a Thing so TerrainDef will only show up as the blueprint/frame Thing for it:
				if (def.entityDefToBuild is TerrainDef)
					blueprintKey += "Terrain";//notranslate
				return blueprintKey.Translate();
			}

			// Categorized things:
			if (def.FirstThingCategory?.LabelCap.ToString() is string label)
			{
				if (label == "TD.MiscCategory".Translate())
					return $"{label} ({def.FirstThingCategory.parent.LabelCap})";
				return label;
			}

			// Catchall for unminifiable buildings.
			if (def.designationCategory?.LabelCap.ToString() is string label2)
			{
				if (label2 == "TD.MiscCategory".Translate())
					return $"{label2} ({ThingCategoryDefOf.Buildings.LabelCap})";
				return label2;
			}

			if (typeof(Pawn).IsAssignableFrom(def.thingClass))
			{
				if (def.race?.FleshType == FleshTypeDefOf.Mechanoid)
				{
					if(def.race.IsWorkMech)
						return "TD.WorkMechanoid".Translate();
					else
						return "MechsSection".Translate();
				}

				return "TD.LivingCategory".Translate();
			}

			if (typeof(Mineable).IsAssignableFrom(def.thingClass))
				return "TD.MineableCategory".Translate();

			// Buildings
			if (typeof(Building).IsAssignableFrom(def.thingClass))
			{
				if(typeof(IThingHolder).IsAssignableFrom(def.thingClass))
					return "TD.ContainerBuildings".Translate();

				// Other uncategorized buildings
				return "TD.Structures".Translate() + " " + "TD.OtherCategory".Translate();
			}

			return "TD.OtherCategory".Translate();
		}

	}

	public class ThingQueryThingDef : ThingQueryCategorizedDropdown<ThingDef, string, ThingQueryThingDef, ThingQueryThingDefCategory>
	{
		public IntRangeUB stackRange;//unknown until sel set

		public ThingQueryThingDef()
		{
			sel = ThingDefOf.WoodLog;
		}
		protected override void PostProcess()
		{
			stackRange.absRange = new(1, sel?.stackLimit ?? 1);
		}
		protected override void PostChosen()
		{
			stackRange.range = new(1, sel.stackLimit);
		}

		public override void ExposeData()
		{
			base.ExposeData();
			
			if(Scribe.mode != LoadSaveMode.Saving || sel?.stackLimit > 1)
				Scribe_Values.Look(ref stackRange.range, "stackRange");
		}
		protected override ThingQuery Clone()
		{
			ThingQueryThingDef clone = (ThingQueryThingDef)base.Clone();
			clone.stackRange = stackRange;
			return clone;
		}


		public override bool AppliesDirectly2(Thing thing) =>
			sel == thing.def &&
			(sel.stackLimit <= 1 || stackRange.Includes(thing.stackCount));


		public override bool Ordered => true;

		public override IEnumerable<ThingDef> AllOptions() =>
			base.AllOptions().Where(ValidDef);
		public override IEnumerable<ThingDef> AvailableOptions() =>
			ContentsUtility.AvailableInGame(t => t.def);

		public override ThingDef IconDefFor(ThingDef o) => o;//duh


		public override string CategoryFor(ThingDef def) => ThingQueryThingDefCategory.CategoryFor(def);


		public override bool DrawCustom(Rect fullRect)
		{
			if (sel == null) return false;

			if (sel.stackLimit > 1)
				return TDWidgets.IntRangeUB(fullRect.RightHalfClamped(row.FinalX), id, ref stackRange);

			return false;
		}
	}

	[StaticConstructorOnStartup]
	public class ThingQueryModded : ThingQueryDropDown<ModContentPack>
	{
		public ThingQueryModded()
		{
			sel = LoadedModManager.RunningMods.First(mod => mod.IsCoreMod);
		}


		public override bool UsesResolveName => true;
		protected override string MakeSaveName() => sel.PackageIdPlayerFacing;

		protected override ModContentPack ResolveName() =>
			LoadedModManager.RunningMods.FirstOrDefault(mod => mod.PackageIdPlayerFacing == selName);


		public override bool AppliesDirectlyTo(Thing thing) =>
			sel == thing.ContentSource;

		public static List<ModContentPack> _runningModsWithThings =
			LoadedModManager.RunningMods.Where(mod => mod.AllDefs.Any(d => d is ThingDef)).ToList();
		public override IEnumerable<ModContentPack> AllOptions() => _runningModsWithThings;

		public override string NameFor(ModContentPack o) => o.Name;


		static ThingQueryModded()
		{
			if (_runningModsWithThings.Count <= 1) //only core
				ThingQueryDefOf.Query_Mod.devOnly = true;
		}
	}


	public class ThingQueryOnScreen : ThingQuery
	{
		public override bool AppliesDirectlyTo(Thing thing) =>
			thing.OccupiedRect().Overlaps(Find.CameraDriver.CurrentViewRect);

		public override bool CurMapOnly => true;
	}


	public class ThingQuerySelectable : ThingQuery
	{
		public override bool AppliesDirectlyTo(Thing thing) =>
			thing.def.selectable;
	}


	public class ThingQueryStat : ThingQueryCategorizedDropdown<StatDef, string>
	{
		FloatRange valueRange;

		public ThingQueryStat()
		{
			sel = StatDefOf.GeneralLaborSpeed;
		}

		protected override void PostChosen()
		{
			valueRange = new FloatRange(sel.minValue, sel.maxValue);
			lBuffer = rBuffer = null;
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref valueRange, "valueRange");
		}
		protected override ThingQuery Clone()
		{
			ThingQueryStat clone = (ThingQueryStat)base.Clone();
			clone.valueRange = valueRange;
			return clone;
		}

		public override bool AppliesDirectlyTo(Thing t) =>
			sel.Worker.ShouldShowFor(StatRequest.For(t)) &&
			valueRange.Includes(t.GetStatValue(sel, cacheStaleAfterTicks: 1));


		public override IEnumerable<StatDef> AllOptions() =>
			base.AllOptions().Where(d => !d.alwaysHide && (DebugSettings.godMode || d.CanShowWithLoadedMods()));


		//should be StatCategoryDef but they have multiple with same name
		public override string NameForCat(string cat) =>
			cat;
		public override string CategoryFor(StatDef def) =>
			def.category.LabelCap;


		public override string NameFor(StatDef def) =>
			def.LabelForFullStatListCap;


		public override bool DrawCustom(Rect fullRect)
		{
			if (sel == null) return false;

			Text.Anchor = TextAnchor.MiddleCenter;
			Widgets.Label(fullRect.RightHalfClamped(row.FinalX),
				$"{valueRange.min.ToStringByStyle(sel.toStringStyle, sel.toStringNumberSense)} - {valueRange.max.ToStringByStyle(sel.toStringStyle, sel.toStringNumberSense)}");
			Text.Anchor = default;

			return false;
		}

		private string lBuffer, rBuffer;
		private string controlNameL, controlNameR;
		protected override bool DrawUnder(Listing_StandardIndent listing, bool locked)
		{
			if (sel == null) return false;

			if (locked) return false;

			listing.NestedIndent();
			listing.Gap(listing.verticalSpacing);

			Rect rect = listing.GetRect(Text.LineHeight);
			Rect lRect = rect.LeftPart(.45f);
			Rect rRect = rect.RightPart(.45f);

			// these wil be the names generated inside TextFieldNumeric
			controlNameL = "TextField" + lRect.y.ToString("F0") + lRect.x.ToString("F0");
			controlNameR = "TextField" + rRect.y.ToString("F0") + rRect.x.ToString("F0");

			FloatRange oldRange = valueRange;
			if (sel.toStringStyle == ToStringStyle.PercentOne || sel.toStringStyle == ToStringStyle.PercentTwo || sel.toStringStyle == ToStringStyle.PercentZero)
			{
				Widgets.TextFieldPercent(lRect, ref valueRange.min, ref lBuffer, float.MinValue, float.MaxValue);
				Widgets.TextFieldPercent(rRect, ref valueRange.max, ref rBuffer, float.MinValue, float.MaxValue);
			}
			/*			else if(sel.toStringStyle == ToStringStyle.Integer)
			{
				Widgets.TextFieldNumeric<int>(lRect, ref valueRangeI.min, ref lBuffer, float.MinValue, float.MaxValue);
				Widgets.TextFieldNumeric<int>(rRect, ref valueRangeI.max, ref rBuffer, float.MinValue, float.MaxValue);
			}*/
			else
			{
				Widgets.TextFieldNumeric<float>(lRect, ref valueRange.min, ref lBuffer, float.MinValue, float.MaxValue);
				Widgets.TextFieldNumeric<float>(rRect, ref valueRange.max, ref rBuffer, float.MinValue, float.MaxValue);
			}


			if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Tab
				 && (GUI.GetNameOfFocusedControl() == controlNameL || GUI.GetNameOfFocusedControl() == controlNameR))
					Event.current.Use();



			listing.NestedOutdent();

			return valueRange != oldRange;
		}

		protected override void DoFocus()
		{
			GUI.FocusControl(controlNameL);
		}

		public override bool Unfocus()
		{
			if (GUI.GetNameOfFocusedControl() == controlNameL || GUI.GetNameOfFocusedControl() == controlNameR)
			{
				UI.UnfocusCurrentControl();
				return true;
			}

			return false;
		}
	}

	public class ThingQueryBuildingCategory : ThingQueryDropDown<DesignationCategoryDef>
	{
		public ThingQueryBuildingCategory() => sel = DesignationCategoryDefOf.Production;

		public override bool AppliesDirectlyTo(Thing thing) =>
			sel == thing.def.designationCategory;

		public override IEnumerable<DesignationCategoryDef> AllOptions() =>
			base.AllOptions().Where(desCatDef => desCatDef.AllResolvedDesignators.Any(d => d is Designator_Build));
	}

	/*
	 * Aborting this attempt at a filter for items that match Alerts
	 * Labels were not easy to get, they were untranslated if expansions weren't active, ugh.
	 * 
	public class ThingQueryAlert : ThingQueryDropDown<Type>
	{
		Alert alert;
		AlertReport report;
		List<Thing> offenders = new();
		int tickReport;

		public ThingQueryAlert()
		{
			sel = typeof(Alert_Heatstroke);
		}

		protected override void PostProcess()
		{
			alert = FindAlert(sel);
			report = default;
			offenders.Clear();
			tickReport = 0;
		}

		public static Alert FindAlert(Type alertType)
		{
			if (alertType == null) return null;

			return Find.Alerts.AllAlerts.FirstOrDefault(a => a.GetType() == alertType);
		}

		private void Update()
		{
			if (alert == null) return;

			if (tickReport == Current.Game.tickManager.TicksGame) return;
			tickReport = Current.Game.tickManager.TicksGame;

			//PostProcess should handle this:
			//if (alert.GetType() != sel)
			//	alert = FindAlert(sel);

			report = alert.GetReport();
			offenders.Clear();
			offenders.AddRange(report.AllCulprits.Where(c => c.HasThing).Select(c => c.Thing));
		}

		public override bool AppliesDirectlyTo(Thing thing)
		{
			Update();
			return offenders.Contains(thing);
		}


		// Preprocess alert list:
		public static bool ValidAlert(Type alertType) =>
			!typeof(Alert_ActionDelay).IsAssignableFrom(alertType) &&
			!typeof(Alert_ActivatorCountdown).IsAssignableFrom(alertType);
		public static readonly List<Type> alertTypes = AlertsReadout.allAlertTypesCached.Where(ValidAlert).ToList();
		public static readonly Dictionary<Type, string> alertNames = alertTypes.ToDictionary(t => t,
			(Type t) =>
			{
				try
				{
					return FindAlert(t).GetLabel();
				}
				catch
				{
					return $"({t?.Name})";
				}
			});


		public override string NameFor(Type o) => alertNames[o];

		public override IEnumerable<Type> Options()
		{
			if (Mod.settings.OnlyAvailable && Find.Alerts.activeAlerts.Count > 0)
				return Find.Alerts.activeAlerts.Select(a => a.GetType()).Where(ValidAlert);

			return alertTypes;
		}		
		// Alert_ActionDelay are silly alerts, let's not go there (and their labels give nullrefs when inactive)
	}*/

	public class ThingQueryReserved : ThingQueryAndOrGroup
	{
		public bool checkReserver;

		public override void ExposeData()
		{
			base.ExposeData();  // please don't have a lot of filters when checkReserver == false
			Scribe_Values.Look(ref checkReserver, "checkReserver");
		}
		protected override ThingQuery Clone()
		{
			ThingQueryReserved clone = (ThingQueryReserved)base.Clone();
			clone.checkReserver = checkReserver;
			return clone;
		}


		public override bool AppliesDirectlyTo(Thing t)
		{
			var reservations = t.MapHeld?.reservationManager.ReservationsReadOnly;
			if (reservations == null)	return false;


			foreach (var res in reservations)
			{
				if (res.Target != t) continue;

				if (res.Faction != Faction.OfPlayer) continue;

				if (!checkReserver) return true;


				// Possibly multiple reservers
				// this filter will only do "any" since it's usually just 1 person
				if (Children.AppliesTo(res.Claimant))
					return true;
			}
			return false;
		}



		protected override bool DrawMain(Rect rect, bool locked, Rect fullRect)
		{
			row.Label(Label); // "Reservation"

			bool changed = row.ButtonTextToggleBool(ref checkReserver, "TD.ReserverMatches".Translate(), "TD.IsReserved".Translate()); // "Is Reservered" / "Reserver matches"

			if (!checkReserver)
				return changed;

			// "Any/All of these filters"
			changed |= ButtonToggleAny();
			row.Label("TD.OfTheseQueries".Translate());

			return changed;
		}

		public override bool AcceptsDrops => checkReserver;
		protected override bool DrawUnder(Listing_StandardIndent listing, bool locked)
		{
			if (!checkReserver)
				return false;

			return base.DrawUnder(listing, locked);
		}
	}

	public class ThingQueryArt : ThingQuery
	{
		public override bool AppliesDirectlyTo(Thing thing) =>
			thing.TryGetComp<CompArt>() is CompArt art && art.Active;
	}

}
