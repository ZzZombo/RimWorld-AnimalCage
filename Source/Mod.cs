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
	public static class CageUtility
	{
		public static IEnumerable<Building_Cage> CagesOnMap(this Map map) => map?.GetComponent<MapComponent_Cage>()?.
			cages ?? Enumerable.Empty<Building_Cage>();
		public static bool HasAssignedCagesIn(this Pawn pawn, Faction captors) => pawn?.FindCage(captors, false) != null;
		public static Building_Cage FindCage(this Pawn pawn, Faction captors, bool onlyIfInside = true)
		{
			if(pawn?.MapHeld is null)
			{
				return null;
			}
			foreach(var cage in pawn.MapHeld.CagesOnMap())
			{
				if(captors is null || cage.Faction == captors)
				{
					if(cage.CageComp?.AssignedPawnsForReading.Contains(pawn) is true &&
						(!onlyIfInside || pawn.Position.IsInside(cage)))
					{
						return cage;
					}
				}
			}
			return null;
		}
		public static bool IsCaptiveOf(this Pawn pawn, Faction captors)
		{
			var cage1 = pawn.PositionHeld.CageHere(pawn.MapHeld);
			if(cage1 is null)
			{
				return false;
			}
			var cage2 = pawn.FindCage(captors ?? cage1.Faction, false);
			return cage1 == cage2 || cage1.Faction == cage2?.Faction;
		}
		public static Building_Cage CageHere(this IntVec3 cell, Map map) => cell.GetEdifice(map) as Building_Cage;
		public static Building_Cage CurrentCage(this Pawn pawn, Map map = null) => pawn.PositionHeld.CageHere(map ?? pawn.MapHeld);
		public static bool Reachable(IntVec3 start, LocalTargetInfo dest, PathEndMode peMode,
			TraverseParms traverseParams)
		{
			var pawn = traverseParams.pawn;
			if(pawn is null || !pawn.IsCaptiveOf(null))
			{
				return false;
			}
			if((peMode == PathEndMode.Touch || peMode == PathEndMode.ClosestTouch) && TouchPathEndModeUtility.
				IsAdjacentOrInsideAndAllowedToTouch(start, dest, pawn.Map))
			{
				return true;
			}
			else if(dest.Cell.IsInside(pawn.CurrentCage()))
			{
				return true;
			}
			return false;
		}
	}
	public class MapComponent_Cage: MapComponent
	{
		public List<Building_Cage> cages = new List<Building_Cage>(0);
		public MapComponent_Cage(Map map) : base(map) { }
	}
	public class Building_Cage: Building
	{
		public CompAssignableToPawn_Cage CageComp => this.GetComp<CompAssignableToPawn_Cage>();
		virtual public IntVec3 EntranceCell => new IntVec3(this.InteractionCell.ToVector3()).ClampInsideRect(this.OccupiedRect());
		public bool forPrisoners = true;
		public ushort pathCost = 8000;
		public bool isBlocking = false;
		public override void SpawnSetup(Map map, bool respawningAfterLoad)
		{
			base.SpawnSetup(map, respawningAfterLoad);
			map.GetComponent<MapComponent_Cage>()?.cages.AddDistinct(this);
		}
		public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
		{
			this.Map.GetComponent<MapComponent_Cage>()?.cages.Remove(this);
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
				defaultLabel = "ZzZomboRW_AnimalCage_OutsidersToggleLabel".Translate(),
				defaultDesc = "ZzZomboRW_AnimalCage_OutsidersToggleDesc".Translate(),
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
			Scribe_Values.Look(ref this.forPrisoners, "forPrisoners", true);
		}
	}
	public class CompAssignableToPawn_Cage: CompAssignableToPawn
	{

		public override IEnumerable<Pawn> AssigningCandidates
		{
			get
			{
				var cage = this.parent as Building_Cage;
				return !(cage?.Spawned is true)
					? Enumerable.Empty<Pawn>()
					: this.parent.Map.mapPawns.AllPawnsSpawned.FindAll(pawn =>
						pawn.BodySize <= this.parent.def.building.bed_maxBodySize &&
						pawn.AnimalOrWildMan() != this.parent.def.building.bed_humanlike &&
						pawn.Faction == cage.Faction != cage.forPrisoners);
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
			foreach(var cage in pawn?.MapHeld?.CagesOnMap() ?? Enumerable.Empty<Building_Cage>())
			{
				cage.CageComp.TryUnassignPawn(pawn);
			}
			base.TryAssignPawn(pawn);
		}
		protected override string GetAssignmentGizmoLabel() => "ZzZomboRW_AnimalCage_AssignToCageLabel".Translate();
		protected override string GetAssignmentGizmoDesc() => "ZzZomboRW_AnimalCage_AssignToCageDesc".Translate();
	}
	public class WorkGiver_RescueToCage: WorkGiver_RescueDowned
	{
		public override Danger MaxPathDanger(Pawn pawn)
		{
			return Danger.Some;
		}
		public override bool ShouldSkip(Pawn pawn, bool forced = false)
		{
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
				if(target.InAggroMentalState || !target.Downed && target.HostileTo(pawn))
				{
					return false;
				}
				var cage = target.FindCage(pawn.Faction);
				if(cage != null)
				{
					result = target.Downed && target.CurJobDef != JobDefOf.LayDown;
				}
				else if(target.HasAssignedCagesIn(pawn.Faction))
				{
					result = target.Downed || target.IsPrisoner && target.guest?.HostFaction == pawn.Faction ||
						target.RaceProps.Animal && target.Faction == pawn.Faction;
				}
			}
			return result && pawn.CanReserve(t, ignoreOtherReservations: forced);
		}

		public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
		{
			if(t is Pawn target)
			{
				//Log.Message($"[WorkGiver_RescueToCage.JobOnThing] {pawn}, {t}, {CompAssignableToPawn_Cage.FindCageFor(victim)}, {CompAssignableToPawn_Cage.FindCageFor(victim, false)}.");
				var cage = target.FindCage(pawn.Faction, true) ?? target.FindCage(pawn.Faction, false);
				if(cage is null || cage.Map != target.Map)
				{
					return null;
				}
				var job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("ZzZomboRW_AnimalCage_Capture"), target, cage);
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
				FailOn(() => !this.pawn.CanReach(this.Cage.EntranceCell, PathEndMode.OnCell, Danger.Deadly)).
				FailOn(() => !(this.Takee.Downed || this.Takee.IsPrisoner && this.Takee.guest?.HostFaction == pawn.Faction ||
					this.Takee.RaceProps.Animal && this.Takee.Faction == pawn.Faction)).
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
			return pawn.Map.mapPawns.AllPawns.FindAll(target => target.FindCage(pawn.Faction) != null);
		}
		public override bool ShouldSkip(Pawn pawn, bool forced = false)
		{
			return (pawn.Map.GetComponent<MapComponent_Cage>()?.cages.Count ?? 0) <= 0;
		}
		public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
		{
			if(pawn is null || !(t is Pawn target) || target.FindCage(pawn.Faction) is null ||
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
				if(!thing.IsSociallyProper(target) || !thing.IsSociallyProper(pawn))
				{
					return null;
				}
				if(this.FoodAvailableInCageTo(pawn, target))
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
		private bool FoodAvailableInCageTo(Pawn pawn, Pawn target)
		{
			var mi = typeof(WorkGiver_Warden_DeliverFood).GetMethod("NutritionAvailableForFrom",
				System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
			if(target.carryTracker?.CarriedThing != null && (float)mi.Invoke(null, new object[] {
				target, target.carryTracker.CarriedThing }) > 0f)
			{
				return true;
			}
			var wantedNutrition = 0f;
			var availableNutrition = 0f;
			var cage = target.FindCage(pawn.Faction);
			foreach(var c in cage.OccupiedRect())
			{
				foreach(var thing in c.GetThingList(cage.Map))
				{
					if(thing is Pawn _pawn)
					{
						if(_pawn.FindCage(pawn.Faction) == cage && _pawn.needs?.food != null &&
							_pawn.needs.food.CurLevelPercentage < _pawn.needs.food.PercentageThreshHungry + 0.02f &&
							(_pawn.carryTracker.CarriedThing is null || !_pawn.WillEat(_pawn.carryTracker.CarriedThing, null, true)))
						{
							wantedNutrition += _pawn.needs.food.NutritionWanted;
						}
					}
					else if(ThingRequestGroup.FoodSourceNotPlantOrTree.Includes(thing.def) &&
						(!thing.def.IsIngestible || thing.def.ingestible.preferability >
							FoodPreferability.DesperateOnlyForHumanlikes))
					{
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
				return pawn.FindCage(this.pawn.Faction) is null || pawn.guest?.CanBeBroughtFood is false;
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
				var cage = pawn.IsCaptiveOf(null) ? pawn.CurrentCage() : null;
				var success = cage != null;
				if(success != this.invert)
				{
					var area = cage.OccupiedRect();
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
