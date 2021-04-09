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
			this.forPrisoners = value;
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
				cage.CageComp?.TryUnassignPawn(pawn);
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
			return !pawn.Map.mapPawns.AllPawnsSpawned.Any(p => this.HasJobOnThing(pawn, p, forced));
		}
		public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
		{
			if(t.Map != pawn.Map)
			{
				return false;
			}
			var result = false;
			if(t is Pawn target)
			{
				var cage = CompAssignableToPawn_Cage.FindCageFor(target);
				if(cage != null)
				{
					result = target.Downed && target.CurJobDef != JobDefOf.LayDown;
				}
				else if(CompAssignableToPawn_Cage.HasFreeCagesFor(target))
				{
					result = target.Downed || target.IsPrisonerOfColony;
				}
			}
			return result && pawn.CanReserve(t, ignoreOtherReservations: forced);
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
					return !this.Cage.CageComp.AssignedPawnsForReading.Contains(this.Takee);
				});
			this.AddFinishAction(delegate
			{
				if(this.pawn.carryTracker.CarriedThing is null)
				{
					return;
				}
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
						target.Notify_Teleported(false);
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
				else
				{
					this.pawn.carryTracker.TryDropCarriedThing(this.pawn.Position, ThingPlaceMode.Direct, out var thing);
				}
			});
			yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch).
				FailOnDespawnedNullOrForbidden(TargetIndex.A).
				FailOnDespawnedNullOrForbidden(TargetIndex.B).
				FailOn(() => !this.pawn.CanReach(this.Cage.InteractionCell, PathEndMode.OnCell, Danger.Deadly)).
				FailOn(() => !(this.Takee.Downed || this.Takee.IsPrisonerOfColony)).
				FailOnSomeonePhysicallyInteracting(TargetIndex.A);
			yield return Toils_Haul.StartCarryThing(TargetIndex.A).
				FailOnDespawnedNullOrForbidden(TargetIndex.B);
			yield return Toils_Goto.GotoCell(this.Cage.OccupiedRect().RandomCell, PathEndMode.ClosestTouch);
			yield return new Toil
			{
				initAction = delegate ()
				{
					var target = this.Takee;
					if(target.playerSettings is null)
					{
						target.playerSettings = new Pawn_PlayerSettings(target);
					}
					if(target.guest != null)
					{
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
				}
			};
			yield return Toils_Reserve.Release(TargetIndex.A);
			yield break;
		}
	}
	public class WorkGiver_Handler_DeliverFood: WorkGiver_Warden_DeliverFood
	{
		public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
		{
			return pawn.Map.mapPawns.AllPawns.FindAll(target => CompAssignableToPawn_Cage.FindCageFor(target) != null);
		}
		public override bool ShouldSkip(Pawn pawn, bool forced = false)
		{
			return (pawn.Map.GetComponent<MapComponent_Cage>()?.cages.Count ?? 0) <= 0;
		}
		public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
		{
			if(pawn is null || !(t is Pawn target) || CompAssignableToPawn_Cage.FindCageFor(target) is null ||
				target.IsFormingCaravan() || t.Spawned && t.Map.designationManager.DesignationOn(t, DesignationDefOf.Slaughter)
				!= null)
			{
				return null;
			}
			if(target.needs?.food is null || target.needs.food.CurLevelPercentage >=
				target.needs.food.PercentageThreshHungry + 0.04f)
			{
				return null;
			}
			if(!FoodUtility.TryFindBestFoodSourceFor(pawn, target, target.needs.food.CurCategory == HungerCategory.Starving,
				out var thing, out var thingDef, false))
			{
				return null;
			}
			var spoonFeeding = target.GetPosture() != PawnPosture.Standing && HealthAIUtility.ShouldSeekMedicalRest(target) &&
				(target.HostFaction == null || target.HostFaction == Faction.OfPlayer && (target.guest?.CanBeBroughtFood ?? true));
			if(!spoonFeeding)
			{
				if(thing.PositionHeld.IsInPrisonCell(pawn.Map))
				{
					return null;
				}
				if(this.FoodAvailableInCageTo(target))
				{
					return null;
				}
			}
			if(!pawn.CanReserveAndReach(target, PathEndMode.ClosestTouch, pawn.NormalMaxDanger(), ignoreOtherReservations: forced))
			{
				return null;
			}
			var nutrition = FoodUtility.GetNutrition(thing, thingDef);
			var job = JobMaker.MakeJob(spoonFeeding ? JobDefOf.FeedPatient : DefDatabase<JobDef>.GetNamed(
				"ZzZomboRW_AnimalCage_DeliverFood"), thing, target);
			job.count = FoodUtility.WillIngestStackCountOf(target, thingDef, nutrition);
			job.targetC = RCellFinder.SpotToChewStandingNear(target, thing);
			return job;
		}
		private bool FoodAvailableInCageTo(Pawn target)
		{
			//Log.Message("[FoodAvailableInCageTo] check #1.");
			var mi = typeof(WorkGiver_Warden_DeliverFood).GetMethod("NutritionAvailableForFrom",
				System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
			if(target.carryTracker?.CarriedThing != null && (float)mi.Invoke(null, new object[] {
				target, target.carryTracker.CarriedThing }) > 0f)
			{
				return true;
			}
			var wantedNutrition = 0f;
			var availableNutrition = 0f;
			var cage = CompAssignableToPawn_Cage.FindCageFor(target);
			//Log.Message($"[FoodAvailableInCageTo] check #2, {cage}.");
			foreach(var c in cage.OccupiedRect())
			{
				foreach(var thing in c.GetThingList(cage.Map))
				{
					if(thing is Pawn pawn)
					{
						//Log.Message($"[FoodAvailableInCageTo] check #3.");
						if(CompAssignableToPawn_Cage.FindCageFor(pawn) == cage && pawn.needs?.food != null &&
							pawn.needs.food.CurLevelPercentage < pawn.needs.food.PercentageThreshHungry + 0.02f &&
							(pawn.carryTracker.CarriedThing is null || !pawn.WillEat(pawn.carryTracker.CarriedThing, null, true)))
						{
							wantedNutrition += pawn.needs.food.NutritionWanted;
						}
					}
					else if(ThingRequestGroup.FoodSourceNotPlantOrTree.Includes(thing.def) &&
						(!thing.def.IsIngestible || thing.def.ingestible.preferability >
							FoodPreferability.DesperateOnlyForHumanlikes))
					{
						//Log.Message($"[FoodAvailableInCageTo] check #4, {target}, {thing}.");
						availableNutrition += (float)mi.Invoke(null, new object[] { target, thing });
					}
				}
			}
			return availableNutrition + 0.5f >= wantedNutrition;
		}
	}
	public class JobDriver_DeliverFood: JobDriver_FoodDeliver
	{
		private bool usingNutrientPasteDispenser;
		private bool eatingFromInventory;
		public override void Notify_Starting()
		{
			base.Notify_Starting();
			this.usingNutrientPasteDispenser = this.TargetThingA is Building_NutrientPasteDispenser;
			this.eatingFromInventory = this.pawn.inventory != null && this.pawn.inventory.Contains(this.TargetThingA);
		}
		protected override IEnumerable<Toil> MakeNewToils()
		{
			this.FailOnDespawnedOrNull(TargetIndex.B);
			if(this.eatingFromInventory)
			{
				yield return Toils_Misc.TakeItemFromInventoryToCarrier(this.pawn, TargetIndex.A);
			}
			else if(this.usingNutrientPasteDispenser)
			{
				yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell).FailOnForbidden(TargetIndex.A);
				yield return Toils_Ingest.TakeMealFromDispenser(TargetIndex.A, this.pawn);
			}
			else
			{
				yield return Toils_Ingest.ReserveFoodFromStackForIngesting(TargetIndex.A, (Pawn)this.TargetThingB);
				yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch).FailOnForbidden(TargetIndex.A);
				yield return Toils_Ingest.PickupIngestible(TargetIndex.A, (Pawn)this.TargetThingB);
			}
			var toil2 = new Toil();
			toil2.initAction = delegate ()
			{
				var actor = toil2.actor;
				var curJob = actor.jobs.curJob;
				actor.pather.StartPath(curJob.targetC, PathEndMode.OnCell);
			};
			toil2.defaultCompleteMode = ToilCompleteMode.PatherArrival;
			toil2.FailOnDestroyedNullOrForbidden(TargetIndex.B);
			toil2.AddFailCondition(delegate
			{
				var pawn = (Pawn)toil2.actor.jobs.curJob.targetB.Thing;
				return CompAssignableToPawn_Cage.FindCageFor(pawn) is null || pawn.guest?.CanBeBroughtFood is false;
			});
			yield return toil2;
			var toil = new Toil();
			toil.initAction = delegate ()
			{
				this.pawn.carryTracker.TryDropCarriedThing(toil.actor.jobs.curJob.targetC.Cell, ThingPlaceMode.Direct, out var thing, null);
			};
			toil.defaultCompleteMode = ToilCompleteMode.Instant;
			yield return toil;
			yield break;
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
				var cage = CompAssignableToPawn_Cage.FindCageFor(pawn);
				var success = cage != null && pawn.Position.GetEdifice(pawn.Map) is Building_Cage;
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
			catch
			{
				pawn.mindState.maxDistToSquadFlag = -1f;
				return ThinkResult.NoJob;
			}
		}
	}
}
