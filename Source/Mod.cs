using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;

internal static class MOD
{
	public const string NAME = "AnimalCages";
}

namespace ZzZomboRW
{
	public class MapComponent_Cage: MapComponent
	{
		public List<Building_Cage> cages = new List<Building_Cage>(0);
		public MapComponent_Cage(Map map) : base(map) { }
		public bool HasFreeCagesFor(Pawn target) => this.FindCageFor(target, false) != null;
		public Building_Cage FindCageFor(Pawn pawn, bool onlyIfInside = true)
		{
			if(pawn is null)
			{
				return null;
			}
			foreach(var cage in this.cages)
			{
				var comp = cage.CageComp;
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
		public CompAssignableToPawn_Cage CageComp => this.GetComp<CompAssignableToPawn_Cage>();
		private bool initialized = false;
		public bool forPrisoners = true;
		public ushort pathCost = 8000;
		//TODO: patch `GenConstruct.TerrainCanSupport()` to disallow changing floor under cages.
		public override void SpawnSetup(Map map, bool respawningAfterLoad)
		{
			base.SpawnSetup(map, respawningAfterLoad);
			map.GetComponent<MapComponent_Cage>()?.cages.AddDistinct(this);
			if(!this.initialized)
			{
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
					if(this.Map.terrainGrid.topGrid[this.Map.cellIndices.CellToIndex(c)] == this.def.building.naturalTerrain)
					{
						this.Map.terrainGrid.RemoveTopLayer(c, true);
					}
				}
			}
			this.initialized = false;
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
		public void SetForPrisoners(bool value)
		{
			if(value == this.forPrisoners)
			{
				return;
			}
			var comp = this.CageComp;
			var list = comp.AssignedPawnsForReading.ToArray();
			foreach(var pawn in list)
			{
				comp.TryUnassignPawn(pawn);
			}
			forPrisoners = value;
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref this.initialized, "initialized", true);
			Scribe_Values.Look(ref this.forPrisoners, "forPrisoners", true);
		}
	}
	public class CompAssignableToPawn_Cage: CompAssignableToPawn
	{
		public static bool HasFreeCagesFor(Pawn pawn) => pawn?.MapHeld?.GetComponent<MapComponent_Cage>()?.
			HasFreeCagesFor(pawn) is true;
		public static Building_Cage FindCageFor(Pawn pawn, bool onlyIfInside = true) => pawn?.MapHeld?.
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
			//Do nothing, first in-first out.
		}
		public override void TryAssignPawn(Pawn pawn)
		{
			if(!this.HasFreeSlot)
			{
				this.TryUnassignPawn(this.AssignedPawnsForReading[0]);
			}
			foreach(var cage in pawn?.MapHeld?.GetComponent<MapComponent_Cage>()?.cages ?? Enumerable.Empty<Building_Cage>())
			{
				var comp = cage.CageComp;
				if(comp?.AssignedPawnsForReading.Contains(pawn) is true)
				{
					comp.TryUnassignPawn(pawn);
				}
			}
			base.TryAssignPawn(pawn);
		}

		protected override string GetAssignmentGizmoLabel()
		{
			//FIXME: Update the translation key.
			return "CommandThingSetOwnerLabel".Translate();
		}

		public static bool Reachable(IntVec3 start, LocalTargetInfo dest, PathEndMode peMode,
			TraverseParms traverseParams)
		{
			var pawn = traverseParams.pawn;
			if(pawn is null)
			{
				return false;
			}
			var cage = CompAssignableToPawn_Cage.FindCageFor(pawn);
			if(cage is null)
			{
				return true;
			}
			if((peMode == PathEndMode.Touch || peMode == PathEndMode.ClosestTouch) && TouchPathEndModeUtility.
				IsAdjacentOrInsideAndAllowedToTouch(start, dest, pawn.Map))
			{
				return true;
			}
			else if(dest.Cell.IsInside(cage))
			{
				return true;
			}
			return false;
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
			return !pawn.Map.mapPawns.AllPawnsSpawned.Any(p => this.HasJobOnThing(pawn, p, forced));
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
				var cage = CompAssignableToPawn_Cage.FindCageFor(victim, true) ??
					CompAssignableToPawn_Cage.FindCageFor(victim, false);
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
		protected Building_Cage Cage => (Building_Cage)this.job.GetTarget(TargetIndex.B).Thing;
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
					var comp = cage.CageComp;
					return !comp.AssignedPawnsForReading.Contains(target);
				});
			this.AddFinishAction(delegate
			{
				var cage = this.Cage;
				var target = this.Takee;
				var comp = cage.CageComp;
				if(!cage.Destroyed && comp.AssignedPawnsForReading.Contains(target))
				{
					if(this.pawn.Position.IsInside(cage))
					{
						if(!target.AnimalOrWildMan())
						{
							target.GetLord()?.Notify_PawnAttemptArrested(target);
							GenClamor.DoClamor(target, 10f, ClamorDefOf.Harm);
							if(!target.IsPrisoner)
							{
								QuestUtility.SendQuestTargetSignals(target.questTags, "Arrested", target.Named("SUBJECT"));
							}
						}
						this.pawn.carryTracker.TryDropCarriedThing(this.pawn.Position, ThingPlaceMode.Direct, out var thing);
						target.Notify_Teleported(false, true);
						target.stances.CancelBusyStanceHard();
						if(target.Downed || HealthAIUtility.ShouldSeekMedicalRest(target))
						{
							target.jobs.StartJob(JobMaker.MakeJob(JobDefOf.LayDown, target.Position),
								JobCondition.InterruptForced, tag: new JobTag?(JobTag.TuckedIntoBed));
						}
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
						this.pawn.carryTracker.TryDropCarriedThing(this.pawn.Position, ThingPlaceMode.Direct, out var thing);
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
					if(this.Takee.playerSettings == null)
					{
						this.Takee.playerSettings = new Pawn_PlayerSettings(this.Takee);
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
			yield return Toils_Goto.GotoCell(this.Cage.OccupiedRect().RandomCell, PathEndMode.ClosestTouch);
			yield return Toils_Reserve.Release(TargetIndex.A);
			yield break;
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
			//Log.Message("[WorkGiver_Handler_DeliverFood] check #1.");
			if(pawn is null || !(t is Pawn target) || CompAssignableToPawn_Cage.FindCageFor(target) is null ||
				target.IsFormingCaravan() || !pawn.CanReserveAndReach(pawn, PathEndMode.OnCell, pawn.NormalMaxDanger(),
				ignoreOtherReservations: forced))
			{
				return null;
			}
			//Log.Message("[WorkGiver_Handler_DeliverFood] check #2.");
			if(!target.Downed || target.needs?.food is null || target.needs.food.CurLevelPercentage >=
				target.needs.food.PercentageThreshHungry + 0.04f)
			{
				return null;
			}
			//Log.Message("[WorkGiver_Handler_DeliverFood] check #3.");
			if(FeedPatientUtility.ShouldBeFed(target))
			{
				return null;
			}
			//Log.Message("[WorkGiver_Handler_DeliverFood] check #4.");
			if(!FoodUtility.TryFindBestFoodSourceFor(pawn, target, target.needs.food.CurCategory == HungerCategory.Starving,
				out var thing, out var thingDef, false))
			{
				return null;
			}
			//Log.Message("[WorkGiver_Handler_DeliverFood] check #5.");
			if(thing.PositionHeld.IsInPrisonCell(pawn.Map))
			{
				return null;
			}
			//Log.Message("[WorkGiver_Handler_DeliverFood] check #6.");
			if(this.FoodAvailableInCageTo(target))
			{
				return null;
			}
			//Log.Message("[WorkGiver_Handler_DeliverFood] checks succeded, making a job.");
			var nutrition = FoodUtility.GetNutrition(thing, thingDef);
			var job = JobMaker.MakeJob(JobDefOf.DeliverFood, thing, target);
			job.count = FoodUtility.WillIngestStackCountOf(target, thingDef, nutrition);
			job.targetC = RCellFinder.SpotToChewStandingNear(target, thing);
			return job;
		}
		private bool FoodAvailableInCageTo(Pawn target)
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
			foreach(var region in room?.Regions ?? Enumerable.Empty<Region>())
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
				foreach(var p in region.ListerThings.ThingsInGroup(ThingRequestGroup.Pawn))
				{
					var pawn = (Pawn)p;
					if(pawn.IsPrisonerOfColony && pawn.Position.IsInside(cage) && pawn.needs?.food != null &&
						pawn.needs.food.CurLevelPercentage < pawn.needs.food.PercentageThreshHungry + 0.02f &&
						(pawn.carryTracker.CarriedThing == null || !pawn.WillEat(pawn.carryTracker.CarriedThing, null, true)))
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
		[NoTranslate]
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
}
