using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;

internal static class MOD
{
	public const string NAME = "MOD";
}

namespace ZzZomboRW
{
	public class MapComponent_Cage: MapComponent
	{
		public List<Building> cages = new List<Building>(0);
		public MapComponent_Cage(Map map) : base(map) { }
		public bool HasFreeCagesFor(Pawn target) => this.FindCageFor(target, false) != null;
		public Building FindCageFor(Pawn pawn, bool onlyIfInside = true)
		{
			if(pawn is null)
			{
				return null;
			}
			foreach(var cage in this.cages)
			{
				var comp = cage?.GetComp<CompAssignableToPawn_Cage>();
				if(comp != null)
				{
					if(comp.AssignedPawnsForReading.Contains(pawn) &&
						(!onlyIfInside || pawn.Position.IsInside(cage)))
					{
						return cage;
					}
				}
			}
			return null;
		}
	}
	public class Building_Cage: Building
	{
		private bool changedTerrain = false;
		public bool forPrisoners = false;
		public ushort pathCost = 8000;
		//TODO: patch `GenConstruct.TerrainCanSupport()` to disallow changing floor under cages.
		public override void SpawnSetup(Map map, bool respawningAfterLoad)
		{
			base.SpawnSetup(map, respawningAfterLoad);
			map.GetComponent<MapComponent_Cage>()?.cages?.AddDistinct(this);
			if(!this.changedTerrain)
			{
				this.SetForPrisoners(true);
				foreach(var c in this.OccupiedRect())
				{
					this.Map.terrainGrid.SetTerrain(c, this.def.building.naturalTerrain ?? TerrainDefOf.WoodPlankFloor);
				}
			}
		}
		public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
		{
			this.Map.GetComponent<MapComponent_Cage>()?.cages?.Remove(this);
			if(mode is DestroyMode.Deconstruct)
			{
				foreach(var c in this.OccupiedRect())
				{
					if(this.Map.terrainGrid.TerrainAt(c) == this.def.building.naturalTerrain)
					{
						this.Map.terrainGrid.RemoveTopLayer(c, true);
					}
				}
			}
			this.changedTerrain = false;
			base.DeSpawn(mode);
		}
		public override ushort PathFindCostFor(Pawn p) => this.pathCost;
		public override IEnumerable<Gizmo> GetGizmos()
		{
			foreach(var gizmo in base.GetGizmos())
			{
				yield return gizmo;
			}
			var command_Toggle = new Command_Toggle
			{
				defaultLabel = "CommandBedSetForPrisonersLabel".Translate(),
				defaultDesc = "CommandBedSetForPrisonersDesc".Translate(),
				icon = ContentFinder<Texture2D>.Get("UI/Commands/ForPrisoners", true),
				isActive = () => this.forPrisoners,
				toggleAction = delegate ()
				{
					var value = !this.forPrisoners;
					(value ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera(null);
					this.SetForPrisoners(value);
				},
				hotKey = KeyBindingDefOf.Misc3,
				turnOffSound = null,
				turnOnSound = null
			};
			yield return command_Toggle;
			yield break;
		}
		public void SetForPrisoners(bool value) => forPrisoners = value;
		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref this.changedTerrain, "changedTerrain", true);
			Scribe_Values.Look(ref this.changedTerrain, "forPrisoners", true);
		}
	}
	public class Area_Cage: Area
	{
		public Area_Cage(AreaManager areaManager) : base(areaManager) { }
		public override string GetUniqueLoadID() => string.Concat(new object[]
		{
			"ZzZomboRW_AnimalCage_",
			this.ID,
		});
		public override string Label => "ZzZomboRW_Area_Cage";
		public override Color Color => SimpleColor.White.ToUnityColor();
		public override int ListPriority => 0;
	}
	public class CompAssignableToPawn_Cage: CompAssignableToPawn
	{
		public static bool HasFreeCagesFor(Pawn pawn) => pawn?.MapHeld?.GetComponent<MapComponent_Cage>()?.
			HasFreeCagesFor(pawn) is true;
		public static Building FindCageFor(Pawn pawn, bool onlyIfInside = true) => pawn?.MapHeld?.
			GetComponent<MapComponent_Cage>()?.FindCageFor(pawn, onlyIfInside);

		public override IEnumerable<Pawn> AssigningCandidates
		{
			get
			{
				var bed = this.parent as Building_Cage;
				return !(bed?.Spawned is true)
					? Enumerable.Empty<Pawn>()
					: this.parent.Map.mapPawns.AllPawnsSpawned.FindAll(pawn =>
						pawn.BodySize <= this.parent.def.building.bed_maxBodySize &&
						pawn.AnimalOrWildMan() != this.parent.def.building.bed_humanlike &&
						pawn.Faction == bed.Faction != bed.forPrisoners);
			}
		}
		protected override void SortAssignedPawns()
		{
		}
		public override void TryAssignPawn(Pawn pawn)
		{
			if(!this.HasFreeSlot)
			{
				this.TryUnassignPawn(this.AssignedPawnsForReading[0]);
			}
			base.TryAssignPawn(pawn);
		}

		protected override string GetAssignmentGizmoLabel()
		{
			//FIXME: Update the translation key.
			return "CommandThingSetOwnerLabel".Translate();
		}
	}
	public class WorkGiver_RescueToCage: WorkGiver_RescueDowned
	{
		public override Danger MaxPathDanger(Pawn pawn)
		{
			return Danger.Some;
		}
		public override bool ShouldSkip(Pawn pawn, bool forced = false)
		{
			//Log.Message($"[WorkGiver_RescueToCage.ShouldSkip] {pawn}, {!pawn.Map.mapPawns.AllPawnsSpawned.Any(p => p.Downed && CompAssignableToPawn_Cage.FindCageFor(p) is null)}.");
			return !pawn.Map.mapPawns.AllPawnsSpawned.Any(p => p.Downed &&
				CompAssignableToPawn_Cage.FindCageFor(p) is null);
		}
		public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
		{
			//var _t = t as Pawn;
			//Log.Message($"[WorkGiver_RescueToCage.HasJobOnThing] {pawn}, {_t}, {_t?.Downed}, {CompAssignableToPawn_Cage.HasFreeCagesFor(_t)}, {CompAssignableToPawn_Cage.FindCageFor(_t)}, {pawn.CanReserve(_t, ignoreOtherReservations: forced)}.");
			return t is Pawn target && target.Downed &&
				target.Map == pawn.Map &&
				CompAssignableToPawn_Cage.HasFreeCagesFor(target) &&
				CompAssignableToPawn_Cage.FindCageFor(target) is null &&
				pawn.CanReserve(target, ignoreOtherReservations: forced);
		}

		public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
		{
			if(t is Pawn victim)
			{
				//Log.Message($"[WorkGiver_RescueToCage.JobOnThing] {pawn}, {t}, {CompAssignableToPawn_Cage.FindCageFor(victim)}, {CompAssignableToPawn_Cage.FindCageFor(victim, false)}.");
				var cage = CompAssignableToPawn_Cage.FindCageFor(victim) ?? CompAssignableToPawn_Cage.FindCageFor(victim, false);
				if(cage is null || cage.Map != victim.Map)
				{
					return null;
				}
				var job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("ZzZomboRW_AnimalCage_Capture"), victim, cage);
				job.count = 1;
				return job;
			}
			return null;
		}
	}
	public class JobDriver_TakeToCage: JobDriver_TakeToBed
	{
		protected Building_Cage Cage
		{
			get
			{
				return (Building_Cage)this.job.GetTarget(TargetIndex.B).Thing;
			}
		}
		public override bool TryMakePreToilReservations(bool errorOnFailed)
		{
			return this.pawn.Reserve(this.Takee, this.job, 1, -1, null, errorOnFailed);
		}
		protected override IEnumerable<Toil> MakeNewToils()
		{
			this.FailOnDestroyedOrNull(TargetIndex.A).
				FailOnDestroyedOrNull(TargetIndex.B).
				FailOnAggroMentalStateAndHostile(TargetIndex.A).
				FailOn(delegate ()
				{
					var cage = this.Cage;
					var target = this.Takee;
					var comp = cage.GetComp<CompAssignableToPawn_Cage>();
					return !comp.AssignedPawnsForReading.Contains(target) ||
						this.pawn.Faction == target.Faction == cage.forPrisoners;
				});
			this.AddFinishAction(delegate
			{
				var cage = this.Cage;
				var target = this.Takee;
				var comp = cage.GetComp<CompAssignableToPawn_Cage>();
				if(this.pawn.Position.IsInside(cage))
				{
					if(!cage.Destroyed && comp.AssignedPawnsForReading.Contains(target))
					{
						IntVec3 position;
						if(this.pawn.Position.IsInside(cage))
						{
							position = cage.Position;
							if(!target.AnimalOrWildMan())
							{
								target.GetLord()?.Notify_PawnAttemptArrested(target);
								GenClamor.DoClamor(target, 10f, ClamorDefOf.Harm);
								if(!target.IsPrisoner)
								{
									QuestUtility.SendQuestTargetSignals(target.questTags, "Arrested", target.Named("SUBJECT"));
								}
							}
							//Log.Warning($"{target}, {target.Drawer}, {target.Drawer.tweener}, {target.pather}.");	 //WTF???
							if(target.pather is null) //WTF???
							{
								PawnComponentsUtility.AddComponentsForSpawn(target);
							}
							target.Notify_Teleported(false, true);
							target.stances.CancelBusyStanceHard();
							target.mindState.Notify_TuckedIntoBed();
							var duty = new PawnDuty(DefDatabase<DutyDef>.GetNamed("ZzZomboRW_AnimalCage_BeingHelpCaptive"),
								cage)
							{
								attackDownedIfStarving = true
							};
							target.mindState.duty = duty;
						}
						else
						{
							position = this.pawn.Position;
						}
						this.pawn.carryTracker.TryDropCarriedThing(position, ThingPlaceMode.Direct, out var thing);
					}
					if(target.IsPrisonerOfColony)
					{
						LessonAutoActivator.TeachOpportunity(ConceptDefOf.PrisonerTab, this.Takee, OpportunityType.GoodToKnow);
					}
				}
			});
			yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch).
				FailOnDespawnedNullOrForbidden(TargetIndex.A).
				FailOnDespawnedNullOrForbidden(TargetIndex.B).
				FailOn(() => !this.pawn.CanReach(this.Cage.InteractionCell, PathEndMode.OnCell, Danger.Deadly,
					false, TraverseMode.ByPawn)).
				FailOn(() => !this.Takee.Downed).
				FailOnSomeonePhysicallyInteracting(TargetIndex.A);
			var toil = Toils_Haul.StartCarryThing(TargetIndex.A).
				FailOnDespawnedNullOrForbidden(TargetIndex.A).
				FailOnDespawnedNullOrForbidden(TargetIndex.B);
			toil.AddPreInitAction(new Action(() =>
			{
				var target = this.Takee;
				if(!target.AnimalOrWildMan())
				{
					if(target.guest is null)
					{
						target.guest = new Pawn_GuestTracker(target);
					}
					if(target.guest.Released is true)
					{
						target.guest.Released = false;
						target.guest.interactionMode = PrisonerInteractionModeDefOf.ReduceResistance;
						GenGuest.RemoveHealthyPrisonerReleasedThoughts(target);
					}
					if(!target.IsPrisonerOfColony)
					{
						target.guest.CapturedBy(Faction.OfPlayer, this.pawn);
					}
				}
			}));
			yield return toil;
			yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.InteractionCell);
			yield return Toils_Goto.GotoThing(TargetIndex.B, this.Cage.InteractionCell.ClampInsideRect(this.Cage.OccupiedRect()));
			yield return Toils_Reserve.Release(TargetIndex.A);
			yield break;
		}

		[StaticConstructorOnStartup]
		internal static class HarmonyHelper
		{
			static HarmonyHelper()
			{
				var harmony = new Harmony($"ZzZomboRW.{MOD.NAME}");
				Harmony.DEBUG = true;
				harmony.PatchAll();
			}
			[HarmonyPriority(Priority.Last)]
			[HarmonyPatch(typeof(Reachability), nameof(Reachability.CanReach), new Type[] { typeof(IntVec3),
				typeof(LocalTargetInfo), typeof(PathEndMode), typeof(TraverseParms) })]
			public static class Reachability_CanReachPatch
			{
				private static void Postfix(ref bool __result, IntVec3 start, LocalTargetInfo dest,
					PathEndMode peMode, TraverseParms traverseParams)
				{
					var pawn = traverseParams.pawn;
					if(!__result || CompAssignableToPawn_Cage.FindCageFor(pawn) is null)
					{
						return;
					}
					if((peMode == PathEndMode.Touch || peMode == PathEndMode.ClosestTouch) && TouchPathEndModeUtility.
						IsAdjacentOrInsideAndAllowedToTouch(pawn.Position, dest, pawn.Map))
					{
						return;
					}
					__result = false;
				}
			}

			[HarmonyPatch(typeof(RegionCostCalculator), "PreciseRegionLinkDistancesNeighborsGetter", Priority.Last)]
			public static class RegionCostCalculator_PathableNeighborIndicesPatch
			{
				private static (Building_Bed, IntVec3) CageOnCell(int index, Map map)
				{
					var cell = map.cellIndices.IndexToCell(index);
					var building = cell.GetEdifice(map);
					if(building is Building_Bed cage)
					{
						var comp = cage.GetComp<CompAssignableToPawn_Cage>();
						if(!(comp is null))
						{
							return (cage, cell);
						}
					}
					return (null, cell);
				}
				private static void Postfix(ref IEnumerable<int> __result, RegionCostCalculator __instance, int node, Region region)
				{
					var map = new Traverse(__instance).Field("map").GetValue<Map>();
					//Log.Warning($"[RegionCostCalculator_PathableNeighborIndicesPatch] {__instance}, {map}, {__result}).", true);
					if(__result is null)
					{
						return;
					}
					var (cage1, cell1) = CageOnCell(node, map);
					var result = new List<int>(8);
					foreach(var idx in __result)
					{
						var (cage2, cell2) = CageOnCell(idx, map);
						//Log.Warning($"[RegionCostCalculator_PathableNeighborIndicesPatch] start: {cell1} ({cage1}), end: {cell2} ({cage2}).", true);
						if(cage1 == cage2)
						{
							result.Add(idx);
						}
						else
						{
							if(cage1 != null)
							{
								var spot = cage1.InteractionCell.ClampInsideRect(cage1.OccupiedRect());
								if(cell1 == spot && cell2 == cage1.InteractionCell ||
									cell2 == spot && cell1 == cage1.InteractionCell)
								{
									result.Add(idx);
								}
							}
							else
							{
								var spot = cage2.InteractionCell.ClampInsideRect(cage2.OccupiedRect());
								if(cell1 == spot && cell2 == cage1.InteractionCell ||
									cell2 == spot && cell1 == cage1.InteractionCell)
								{
									result.Add(idx);
								}
							}
						}
					}
					__result = result;
				}
			}

			[HarmonyPriority(Priority.Last)]
			[HarmonyPatch(typeof(PathFinder), nameof(PathFinder.FindPath), new Type[] { typeof(IntVec3),
				typeof(LocalTargetInfo), typeof(TraverseParms), typeof(PathEndMode) })]
			public static class PathFinder_FindPathPatch
			{
				public static Building_Cage CageOnCell(IntVec3 cell, Map map)
				{
					var edifice = cell.GetEdifice(map);
					if(edifice is Building_Cage cage)
					{
						var comp = cage.GetComp<CompAssignableToPawn_Cage>();
						if(!(comp is null))
						{
							return cage;
						}
					}
					return null;
				}
				private static void Prefix(PathFinder __instance, ref Building_Cage[] __state,
					Map ___map, ref IntVec3 start, ref LocalTargetInfo dest, TraverseParms traverseParms, PathEndMode peMode)
				{
					var map = ___map;
					var cage1 = CageOnCell(start, map);
					var cage2 = CageOnCell(dest.Cell, map);
					if(cage1 == cage2)
					{
						if(cage1 is Building_Cage _cage)
						{
							_cage.pathCost = 0;
						}
						return;
					}
					__state = new Building_Cage[] { cage1 as Building_Cage, cage2 as Building_Cage };
					if(cage1 != null)
					{
						var spot1 = cage1.InteractionCell;
						var spot2 = new IntVec3(spot1.ToVector3()).ClampInsideRect(cage1.OccupiedRect());
						//var s = $"[PF1 (prefix)] `{cage1}`!=`{cage2}`, start={start}, dest={dest}, spot1={spot1}, spot2=";
						dest = start == spot2 ? spot1 : spot2;
						//s += $"{dest}.";
						//Log.Message(s);
						if(cage1 is Building_Cage _cage)
						{
							_cage.pathCost = 0;
						}
					}
					else
					{
						var spot1 = cage2.InteractionCell;
						//var s = $"[PF2 (prefix)] `{cage1}`!=`{cage2}`, start={start}, dest={dest}, spot1={spot1}, spot2=";
						dest = start == spot1 ? new IntVec3(spot1.ToVector3()).ClampInsideRect(cage2.OccupiedRect()) : spot1;
						//s += $"{dest}.";
						//Log.Message(s);
						if(cage2 is Building_Cage _cage)
						{
							_cage.pathCost = 8000;
						}
					}
				}
				private static void Postfix(ref PawnPath __result, PathFinder __instance, ref
					Building_Cage[] __state, IntVec3 start, LocalTargetInfo dest, TraverseParms traverseParms, PathEndMode peMode)
				{
					//Log.Message($"[PF (postfix)] result={__result}.");
					var (cage1, cage2) = (__state?[0], __state?[1]);
					//Log.Message($"[PF (postfix)] cage1={cage1}, cage2={cage2}.");
					/// HERE BE DRAGONS! This is a giant hack, as sometimes the path finder returns paths of exactly
					/// one node, of the pawn's current position (the `start` parameter), when going into or out a cage.
					/// It doesn't always happen, and so I couldn't determine the exact cause of this errant behavior.
					/// Instead I just insert the two entrance cells into the returned path as appropriate.
					if(cage1 != cage2 && __result.NodesLeftCount is 1 && dest != start)
					{
						//Log.Message($"[PF (postfix)] {dest}!={(LocalTargetInfo)start}, patching the path.");
						var cage = cage1 ?? cage2;
						var f = new Traverse(__result).Field("nodes");
						var nodes = f.GetValue<List<IntVec3>>();
						var spot1 = cage.InteractionCell;
						var spot2 = new IntVec3(spot1.ToVector3()).ClampInsideRect(cage.OccupiedRect());
						var newNodes = new List<IntVec3> { spot2, spot1 };
						//Log.Message($"[PF (postfix)] pawn pos.={traverseParms.pawn.Position} (start={start}), interact. cell={newNodes[1]}, entran. cell={newNodes[0]}.");
						newNodes.RemoveAll((c) => c == start);
						if(cage == cage1)
						{
							newNodes.Reverse();
						}
						nodes.InsertRange(0, newNodes);
						//Log.Message($"[PF (postfix)] new nodes=[{string.Join(", ", nodes)}], patching the path complete.");
						f.SetValue(nodes);
						f = new Traverse(__result).Field("curNodeIndex");
						f.SetValue(f.GetValue<int>() + newNodes.Count);
						//Log.Message($"[PF (postfix)] {__result}, [{string.Join(", ", nodes)}], patching the path complete.");
					}
					foreach(var cage in __state ?? Array.Empty<Building_Cage>())
					{
						if(cage != null)
						{
							cage.pathCost = 8000;
						}
					}
				}
			}
		}
	}
	public class WorkGiver_Handler_DeliverFood: WorkGiver_Warden
	{
		public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
		{
			return pawn.Map.mapPawns.AllPawns.FindAll(target => CompAssignableToPawn_Cage.FindCageFor(target) != null);
		}
		public override bool ShouldSkip(Pawn pawn, bool forced = false)
		{
			var count = pawn.Map.GetComponent<MapComponent_Cage>()?.cages?.Count;
			return count is null || count < 1;
		}
		public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
		{
			if(pawn is null || !(t is Pawn target) || CompAssignableToPawn_Cage.FindCageFor(target) is null ||
				target.IsFormingCaravan() || !pawn.CanReserveAndReach(pawn, PathEndMode.OnCell, pawn.NormalMaxDanger(),
				ignoreOtherReservations: forced))
			{
				return null;
			}
			if(!target.Downed || target.needs.food.CurLevelPercentage >=
				target.needs.food.PercentageThreshHungry + 0.04f)
			{
				return null;
			}
			if(FeedPatientUtility.ShouldBeFed(target))
			{
				return null;
			}
			if(!FoodUtility.TryFindBestFoodSourceFor(pawn, target, target.needs.food.CurCategory == HungerCategory.Starving,
				out var thing, out var thingDef, false))
			{
				return null;
			}
			if(this.FoodAvailableInRoomTo(target))
			{
				return null;
			}
			var nutrition = FoodUtility.GetNutrition(thing, thingDef);
			var job = JobMaker.MakeJob(JobDefOf.DeliverFood, thing, target);
			job.count = FoodUtility.WillIngestStackCountOf(target, thingDef, nutrition);
			job.targetC = RCellFinder.SpotToChewStandingNear(target, thing);
			return job;
		}
		private bool FoodAvailableInRoomTo(Pawn target)
		{
			var mi = base.GetType().GetMethod("NutritionAvailableForFrom",
				System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
			if(target.carryTracker.CarriedThing != null && (float)mi.Invoke(null, new object[] {
				target, target.carryTracker.CarriedThing }) > 0f)
			{
				return true;
			}
			var wantedNutrition = 0f;
			var availableNutrition = 0f;
			var room = target.GetRoom(RegionType.Set_Passable);
			var cage = CompAssignableToPawn_Cage.FindCageFor(target);
			foreach(var region in room?.Regions)
			{
				foreach(var thing in region.ListerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree))
				{
					if(thing.Position.IsInside(cage))
					{
						if(!thing.def.IsIngestible || thing.def.ingestible.preferability >
							FoodPreferability.DesperateOnlyForHumanlikes)
						{
							availableNutrition += (float)mi.Invoke(null, new object[] { target, thing });
						}
					}
				}
				var list2 = region.ListerThings.ThingsInGroup(ThingRequestGroup.Pawn);
				for(var k = 0; k < list2.Count; k++)
				{
					var pawn = list2[k] as Pawn;
					if(pawn.IsPrisonerOfColony && pawn.needs.food.CurLevelPercentage < pawn.needs.food.PercentageThreshHungry + 0.02f && (pawn.carryTracker.CarriedThing == null || !pawn.WillEat(pawn.carryTracker.CarriedThing, null, true)))
					{
						wantedNutrition += pawn.needs.food.NutritionWanted;
					}
				}
			}
			return availableNutrition + 0.5f >= wantedNutrition;
		}
	}
	public class ThinkNode_Duty: ThinkNode
	{
		private string dutyDef;
		public override ThinkResult TryIssueJobPackage(Pawn pawn, JobIssueParams jobParams)
		{
			var duty = new PawnDuty(DefDatabase<DutyDef>.GetNamed(this.dutyDef));
			pawn.mindState.duty = duty;
			return ThinkResult.NoJob;
		}
	}
	public class ThinkNode_ConditionalInsideCage: ThinkNode_Priority
	{
		public bool invert;
		public override ThinkResult TryIssueJobPackage(Pawn pawn, JobIssueParams jobParams)
		{
			try
			{
				if(pawn.mindState.duty?.focus.HasThing is true)
				{
					var success = pawn.mindState.duty?.focus.HasThing is true &&
						pawn.Position.IsInside(pawn.mindState.duty.focus.Thing);
					if(success != this.invert)
					{
						var area = pawn.mindState.duty.focus.Thing.OccupiedRect();
						pawn.mindState.maxDistToSquadFlag = Math.Max(area.Width / 2, area.Height / 2);
						return base.TryIssueJobPackage(pawn, jobParams);
					}
					else
					{
						pawn.mindState.maxDistToSquadFlag = -1;
						return ThinkResult.NoJob;
					}
				}
			}
			finally
			{
				pawn.mindState.maxDistToSquadFlag = -1f;
			}
			return ThinkResult.NoJob;
		}
	}
	//public class CompProperties_X: CompProperties
	//{
	//	public bool enabled = true;
	//	public int data = -1;
	//	public CompProperties_X()
	//	{
	//		this.compClass = typeof(CompX);
	//		Log.Message($"[{this.GetType().FullName}] Initialized:\n" +
	//			$"\tCurrent ammo: {this.data};\n" +
	//			$"\tEnabled: {this.enabled}.");
	//	}

	//}
	//public class CompX: ThingComp
	//{
	//	public CompProperties_X Props => (CompProperties_X)this.props;
	//	public bool Enabled => this.Props.enabled;
	//	public override void Initialize(CompProperties props)
	//	{
	//		base.Initialize(props);
	//		Log.Message($"[{this.GetType().FullName}] Initialized for {this.parent}:\n" +
	//			$"\tData: {this.Props.data};\n" +
	//			$"\tEnabled: {this.Props.enabled}.");
	//	}

	//	public override void PostExposeData()
	//	{
	//		Scribe_Values.Look(ref this.Props.data, "data", 1, false);
	//		Scribe_Values.Look(ref this.Props.enabled, "enabled", true, false);
	//	}

	//	[StaticConstructorOnStartup]
	//	internal static class HarmonyHelper
	//	{
	//		static HarmonyHelper()
	//		{
	//			Harmony harmony = new Harmony($"ZzZomboRW.{MOD.NAME}");
	//			harmony.PatchAll();
	//		}

	//		[HarmonyPatch(typeof(object), nameof(object.Equals))]
	//		public static class Class_MethodPatch
	//		{
	//			private static void Postfix(ref object __result, object __instance)
	//			{
	//			}
	//		}
	//	}
	//}
}
