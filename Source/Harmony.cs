using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZzZomboRW
{
	[StaticConstructorOnStartup]
	internal static class HarmonyHelper
	{
		static HarmonyHelper()
		{
			var harmony = new Harmony($"ZzZomboRW.{MOD.NAME}");
			//Harmony.DEBUG = true;
			harmony.PatchAll();
		}

		[HarmonyPatch(typeof(CellFinder), nameof(CellFinder.TryFindRandomReachableCellNear), Priority.Last)]
		public static class CellFinder_TryFindRandomReachableCellNearPatch
		{
			private static void Postfix(ref bool __result, IntVec3 root, Map map, float radius, TraverseParms traverseParms,
				Predicate<IntVec3> cellValidator, Predicate<Region> regionValidator, ref IntVec3 result, int maxRegions)
			{
				if(!__result || map is null || traverseParms.pawn is null)
				{
					return;
				}
				var cage = CompAssignableToPawn_Cage.FindCageFor(traverseParms.pawn);
				if(cage != null)
				{
					var cells = cage.OccupiedRect().InRandomOrder().ToList();
					result = IntVec3.Invalid;
					foreach(var c in cells)
					{
						if(traverseParms.pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly) &&
							(cellValidator is null || cellValidator(c)) &&
							(regionValidator is null || regionValidator(c.GetRegion(map, RegionType.Set_Passable))))
						{
							result = c;
							break;
						}
					}
					__result = result != IntVec3.Invalid;
				}
			}
		}

		[HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.ShouldBeFedBySomeone), Priority.Last)]
		public static class FoodUtility_ShouldBeFedBySomeonePatch
		{
			private static void Postfix(ref bool __result, Pawn pawn)
			{
				if(__result || pawn is null)
				{
					return;
				}
				if(CompAssignableToPawn_Cage.FindCageFor(pawn) != null)
				{
					__result = pawn.GetPosture() != PawnPosture.Standing && HealthAIUtility.ShouldSeekMedicalRest(pawn) &&
						(pawn.HostFaction == null || pawn.HostFaction == Faction.OfPlayer && (pawn.guest?.CanBeBroughtFood ?? true));
				}
			}
		}

		[HarmonyPatch(typeof(ForbidUtility), nameof(ForbidUtility.CaresAboutForbidden), Priority.Last)]
		public static class ForbidUtility_CaresAboutForbiddenPatch
		{
			private static void Postfix(ref bool __result, Pawn pawn, bool cellTarget)
			{
				if(__result)
				{
					return;
				}
				if(CompAssignableToPawn_Cage.FindCageFor(pawn) != null)
				{
					__result = true;
				}
			}
		}

		[HarmonyPatch(typeof(ForbidUtility), nameof(ForbidUtility.InAllowedArea), Priority.Last)]
		public static class ForbidUtility_InAllowedAreaPatch
		{
			private static void Postfix(ref bool __result, IntVec3 c, Pawn forPawn)
			{
				if(!__result)
				{
					return;
				}
				var cage = CompAssignableToPawn_Cage.FindCageFor(forPawn);
				if(cage != null)
				{
					__result = CompAssignableToPawn_Cage.Reachable(forPawn.Position, c, PathEndMode.OnCell,
						TraverseParms.For(forPawn, mode: TraverseMode.ByPawn));
				}
			}
		}

		[HarmonyPatch(typeof(ForbidUtility), nameof(ForbidUtility.IsForbiddenEntirely), Priority.Last)]
		public static class ForbidUtility_IsForbiddenEntirelyPatch
		{
			private static void Postfix(ref bool __result, Region r, Pawn pawn)
			{
				if(__result)
				{
					return;
				}
				var cage = CompAssignableToPawn_Cage.FindCageFor(pawn);
				if(cage != null)
				{
					__result = r != cage.GetRegion();
				}
			}
		}

		[HarmonyPriority(Priority.Last)]
		[HarmonyPatch(typeof(ForbidUtility), nameof(ForbidUtility.IsForbidden), new Type[] { typeof(IntVec3),
				typeof(Pawn) })]
		public static class ForbidUtility_IsForbiddenPatch
		{
			private static void Postfix(ref bool __result, IntVec3 c, Pawn pawn)
			{
				if(__result)
				{
					return;
				}
				if(CompAssignableToPawn_Cage.FindCageFor(pawn) != null)
				{
					__result = !CompAssignableToPawn_Cage.Reachable(pawn.Position, c, PathEndMode.ClosestTouch,
						TraverseParms.For(pawn, mode: TraverseMode.ByPawn));
				}
			}
		}

		[HarmonyPriority(Priority.Last)]
		[HarmonyPatch(typeof(ForbidUtility), nameof(ForbidUtility.IsForbidden), new Type[] { typeof(Thing),
				typeof(Pawn) })]
		public static class ForbidUtility_IsForbiddenPatch2
		{
			private static void Postfix(ref bool __result, Thing t, Pawn pawn)
			{
				if(__result)
				{
					return;
				}
				if(CompAssignableToPawn_Cage.FindCageFor(pawn) != null)
				{
					__result = !CompAssignableToPawn_Cage.Reachable(pawn.Position, t, PathEndMode.ClosestTouch,
						TraverseParms.For(pawn, mode: TraverseMode.ByPawn));
				}
			}
		}

		[HarmonyPatch(typeof(GenClosest), nameof(GenClosest.ClosestThingReachable), Priority.Last)]
		public static class GenClosest_ClosestThingReachablePatch
		{
			private static void Postfix(ref Thing __result, IntVec3 root, Map map, ThingRequest thingReq,
				PathEndMode peMode, TraverseParms traverseParams, float maxDistance,
				Predicate<Thing> validator, IEnumerable<Thing> customGlobalSearchSet,
				int searchRegionsMin, int searchRegionsMax, bool forceAllowGlobalSearch,
				RegionType traversableRegionTypes, bool ignoreEntirelyForbiddenRegions)
			{
				if(__result is null || traverseParams.pawn is null)
				{
					return;
				}
				if(CompAssignableToPawn_Cage.FindCageFor(traverseParams.pawn) != null &&
					!CompAssignableToPawn_Cage.Reachable(root, __result, peMode, traverseParams))
				{
					__result = null;
				}
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
				if(traverseParms.pawn is null)
				{
					return;
				}
				var map = ___map;
				var cage1 = CageOnCell(start, map);
				if(cage1 != null && (peMode == PathEndMode.Touch || peMode == PathEndMode.ClosestTouch) &&
					TouchPathEndModeUtility.IsAdjacentOrInsideAndAllowedToTouch(start, dest, traverseParms.pawn.Map))
				{
					dest = dest.Cell.ClampInsideRect(cage1.OccupiedRect());
					return;
				}
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
				if(traverseParms.pawn is null)
				{
					return;
				}
				var (cage1, cage2) = (__state?[0], __state?[1]);
				//Log.Message($"[PF (postfix)] result={__result}, cage1={cage1}, cage2={cage2}.");
				if(cage1 != cage2)
				{
					var cage = cage1 ?? cage2;
					if(cage1 != null && cage1 == CompAssignableToPawn_Cage.FindCageFor(traverseParms.pawn))
					{
						Log.Warning($"[PF (postfix)] Attempt to leave cage prevented: {traverseParms.pawn}, cage1={cage1}," +
							$" cage2={cage2}, job=`{traverseParms.pawn.CurJob}`, result={__result}.");
						__result.ReleaseToPool();
						__result = PawnPath.NotFound;
					}
					else if(__result.NodesLeftCount is 1 && dest != start)
					{
						/// HERE BE DRAGONS! This is a giant hack, as sometimes the path finder returns paths of exactly
						/// one node, of the pawn's current position (the `start` parameter), when going into or out a cage.
						/// It doesn't always happen, and so I couldn't determine the exact cause of this errant behavior.
						/// Instead I just insert the two entrance cells into the returned path as appropriate.
						//Log.Message($"[PF (postfix)] {dest}!={(LocalTargetInfo)start}, patching the path.");
						var spot1 = cage.InteractionCell;
						var spot2 = new IntVec3(spot1.ToVector3()).ClampInsideRect(cage.OccupiedRect());
						var newNodes = new List<IntVec3> { spot2, spot1 };
						//Log.Message($"[PF (postfix)] pawn pos.={traverseParms.pawn.Position} (start={start}), interact. cell={newNodes[1]}, entran. cell={newNodes[0]}.");
						newNodes.RemoveAll((c) => c == start);
						if(cage == cage1)
						{
							newNodes.Reverse();
						}
						var f = new Traverse(__result).Field("nodes");
						var nodes = f.GetValue<List<IntVec3>>();
						nodes.InsertRange(0, newNodes);
						//Log.Message($"[PF (postfix)] new nodes=[{string.Join(", ", nodes)}], patching the path complete.");
						f.SetValue(nodes);
						f = new Traverse(__result).Field("curNodeIndex");
						f.SetValue(f.GetValue<int>() + newNodes.Count);
						//Log.Message($"[PF (postfix)] {__result}, [{string.Join(", ", nodes)}], patching the path complete.");
					}
				}
				foreach(var cage in __state ?? Enumerable.Empty<Building_Cage>())
				{
					if(cage != null)
					{
						cage.pathCost = 8000;
					}
				}
			}
		}

		[HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.HasHediffsNeedingTendByPlayer), Priority.Last)]
		public static class Pawn_HealthTracker_HasHediffsNeedingTendByPlayerPatch
		{
			private static void Postfix(ref bool __result, ref Pawn_HealthTracker __instance, ref Pawn ___pawn, bool forAlert)
			{
				if(__result || forAlert)
				{
					return;
				}
				var pawn = ___pawn;
				if(pawn.AnimalOrWildMan() && pawn.GetPosture() != PawnPosture.Standing &&
					__instance.HasHediffsNeedingTend(forAlert) && CompAssignableToPawn_Cage.FindCageFor(pawn) != null)
				{
					__result = true;
				}
			}
		}

		[HarmonyPatch(typeof(PawnDownedWiggler), nameof(PawnDownedWiggler.WigglerTick), Priority.Last)]
		public static class PawnDownedWiggler_WigglerTickPatch
		{
			private static void Prefix(Pawn ___pawn, PawnDownedWiggler __instance)
			{
				var pawn = ___pawn;
				if(!pawn.Downed || !pawn.Spawned)
				{
					return;
				}
				if(CompAssignableToPawn_Cage.FindCageFor(pawn) != null)
				{
					__instance.ticksToIncapIcon = 200;
				}
			}
		}

		[HarmonyPriority(Priority.Last)]
		[HarmonyPatch(typeof(Reachability), nameof(Reachability.CanReach), new Type[] { typeof(IntVec3),
				typeof(LocalTargetInfo), typeof(PathEndMode), typeof(TraverseParms) })]
		public static class Reachability_CanReachPatch
		{
			private static void Postfix(ref bool __result, IntVec3 start, LocalTargetInfo dest,
				PathEndMode peMode, TraverseParms traverseParams)
			{
				if(!__result || traverseParams.pawn is null)
				{
					return;
				}
				//Log.Warning($"[Reachability_CanReachPatch] {pawn} at {pawn.Position} inside {CompAssignableToPawn_Cage.FindCageFor(pawn)} " +
				//	$"attempted to reach {dest} ({dest.Cell}) from {start} with job `{pawn.CurJob}` and `peMode`={peMode}.");
				if(CompAssignableToPawn_Cage.FindCageFor(traverseParams.pawn) != null)
				{
					__result = CompAssignableToPawn_Cage.Reachable(start, dest, peMode, traverseParams);
				}
			}
		}

		[HarmonyPatch(typeof(RCellFinder), nameof(RCellFinder.SpotToChewStandingNear), Priority.Last)]
		public static class Reachability_SpotToChewStandingNearPatch
		{
			private static bool Prefix(ref IntVec3 __result, Pawn pawn, Thing ingestible)
			{
				var cage = CompAssignableToPawn_Cage.FindCageFor(pawn);
				if(cage is null)
				{
					return true;
				}
				__result = cage.OccupiedRect().RandomCell;
				return false;
			}
		}

		[HarmonyPatch(typeof(ReachabilityWithinRegion), nameof(ReachabilityWithinRegion.ThingFromRegionListerReachable), Priority.Last)]
		public static class ReachabilityWithinRegion_ThingFromRegionListerReachablePatch
		{
			private static void Postfix(ref bool __result, Thing thing, Region region, PathEndMode peMode, Pawn traveler)
			{
				if(!__result || traveler is null)
				{
					return;
				}
				if(CompAssignableToPawn_Cage.FindCageFor(traveler) != null)
				{
					__result = CompAssignableToPawn_Cage.Reachable(traveler.Position, thing, peMode, TraverseParms.For(traveler,
						mode: TraverseMode.ByPawn));
				}
			}
		}

		[HarmonyPatch(typeof(RegionCostCalculator), "PreciseRegionLinkDistancesNeighborsGetter", Priority.Last)]
		public static class RegionCostCalculator_PathableNeighborIndicesPatch
		{
			private static (Building_Cage, IntVec3) CageOnCell(int index, Map map)
			{
				var cell = map.cellIndices.IndexToCell(index);
				var building = cell.GetEdifice(map);
				if(building is Building_Cage cage)
				{
					var comp = cage.CageComp;
					if(!(comp is null))
					{
						return (cage, cell);
					}
				}
				return (null, cell);
			}
			private static void Postfix(ref IEnumerable<int> __result, RegionCostCalculator __instance, Map ___map,
				int node, Region region)
			{
				//Log.Warning($"[RegionCostCalculator_PathableNeighborIndicesPatch] {__instance}, {map}, {__result}).", true);
				if(__result is null)
				{
					return;
				}
				var map = ___map;
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
		[HarmonyPatch(typeof(SocialProperness), nameof(SocialProperness.IsSociallyProper), new Type[] {
			typeof(Thing), typeof(Pawn), typeof(bool), typeof(bool) })]
		public static class SocialProperness_IsSociallyProperPatch
		{
			private static void Postfix(ref bool __result, Thing t, Pawn p, bool forPrisoner, bool animalsCare)
			{
				if(p is null || !(t.def.socialPropernessMatters && t.Spawned))
				{
					return;
				}
				var position = t.def.hasInteractionCell ? t.InteractionCell : t.Position;
				if(!position.IsInPrisonCell(t.MapHeld))
				{
					var cage1 = CompAssignableToPawn_Cage.FindCageFor(p);
					var cage2 = t.Position.GetEdifice(t.Map) as Building_Cage;
					if(cage1 == cage2)
					{
						__result = !animalsCare && p.AnimalOrWildMan() || cage1 is null != forPrisoner;
					}
					else
					{
						__result = !animalsCare && p.AnimalOrWildMan();
					}
				}
			}
		}

		[HarmonyPatch(typeof(WorkGiver_Tend), nameof(WorkGiver_Tend.HasJobOnThing), Priority.Last)]
		public static class WorkGiver_Tend_HasJobOnThingPatch
		{
			private static void Postfix(ref bool __result, ref WorkGiver_Tend __instance, Pawn pawn, Thing t, bool forced)
			{
				if(__result || t == pawn && !(__instance is WorkGiver_TendSelf))
				{
					return;
				}
				if(t is Pawn target && CompAssignableToPawn_Cage.FindCageFor(target) != null &&
					!pawn.WorkTypeIsDisabled(WorkTypeDefOf.Doctor) &&
					(!__instance.def.tendToHumanlikesOnly || target.RaceProps.Humanlike && !target.IsWildMan()) &&
					(!__instance.def.tendToAnimalsOnly || target.AnimalOrWildMan()) &&
					target.GetPosture() != PawnPosture.Standing && HealthAIUtility.ShouldBeTendedNowByPlayer(target) &&
					pawn.CanReserve(target, 1, -1, null, forced))
				{
					__result = true;
				}
			}
		}

		[HarmonyPatch(typeof(WorkGiver_Tend), nameof(WorkGiver_Tend.GoodLayingStatusForTend), Priority.Last)]
		public static class WorkGiver_Tend_GoodLayingStatusForTendPatch
		{
			private static void Postfix(ref bool __result, Pawn patient, Pawn doctor)
			{
				if(__result)
				{
					return;
				}
				if(CompAssignableToPawn_Cage.FindCageFor(patient) != null && patient.GetPosture() != PawnPosture.Standing)
				{
					__result = true;
				}
			}
		}
	}
}
