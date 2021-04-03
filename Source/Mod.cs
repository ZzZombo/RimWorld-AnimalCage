using UnityEngine;
using HarmonyLib;
using Verse;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse.AI;
using Verse.AI.Group;
using System;
using RimWorld.Planet;

internal static class MOD
{
	public const string NAME = "MOD";
}

namespace ZzZomboRW
{
	public class MapComponent_Cage: MapComponent
	{
		public List<Building_Bed> cages = new List<Building_Bed>();
		public MapComponent_Cage(Map map) : base(map)
		{
		}
		public bool HasFreeCagesFor(Pawn target) => this.FindCageFor(target) != null;
		public Building_Bed FindCageFor(Pawn pawn, bool onlyIfInside = true)
		{
			foreach(var cage in cages)
			{
				var comp = cage.GetComp<CompAssignableToPawn_Cage>();
				if(comp != null)
				{
					if(cage.GetAssignedPawns().Contains(pawn) && comp.HasFreeSlot &&
						(!onlyIfInside || cage.OccupiedRect().Contains(pawn.Position)))
					{
						return cage;
					}
				}
			}
			return null;
		}
	}
	public class Building_Cage: Building_Bed
	{
		private bool changedTerrain = false;
		//TODO: patch `GenConstruct.TerrainCanSupport()` to disallow changing floor under cages.
		public override void SpawnSetup(Map map, bool respawningAfterLoad)
		{
			base.SpawnSetup(map, respawningAfterLoad);
			map.GetComponent<MapComponent_Cage>().cages.AddDistinct(this);
			if(!this.changedTerrain)
			{
				foreach(var c in this.OccupiedRect())
				{
					this.Map.terrainGrid.SetTerrain(c, this.def.building.naturalTerrain ?? TerrainDefOf.WoodPlankFloor);
				}
			}
		}
		public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
		{
			base.DeSpawn(mode);
			this.Map.GetComponent<MapComponent_Cage>().cages.Remove(this);
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
		}
		public override bool BlocksPawn(Pawn p)
		{
			if(base.BlocksPawn(p))
			{
				return true;
			}
			var comp = this.GetComp<CompAssignableToPawn_Cage>();
			if(!(comp is null))
			{
				return !this.GetAssignedPawns().Contains(p) && (!p.CurJob?.AnyTargetOutsideArea(comp.Area) ?? true);
			}
			return false;
		}
		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref this.changedTerrain, "changedTerrain", true);
		}
	}
	public class Area_Cage: Area
	{
		public bool valid = false;
		public Area_Cage(AreaManager manager) : base(manager)
		{
			this.valid = true;
		}
		public override string GetUniqueLoadID() => string.Concat(new object[]
		{
			"ZzZomboRW_AnimalCage_",
			this.ID,
		});
		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref this.valid, "valid");
		}

		public override bool Mutable => !(this.valid is true);

		public override string Label => "ZzZomboRW_Area_Cage".Translate();

		public override Color Color => SimpleColor.White.ToUnityColor();

		public override int ListPriority => 0;
	}
	public class CompAssignableToPawn_Cage: CompAssignableToPawn_Bed
	{
		public static bool HasFreeCagesFor(Pawn pawn) => pawn.Map.GetComponent<MapComponent_Cage>().
			FindCageFor(pawn) != null;
		public static Building_Bed FindCageFor(Pawn pawn, bool onlyIfInside = true)
		{
			foreach(var cage in pawn.Map.GetComponent<MapComponent_Cage>().cages)
			{
				var comp = cage.GetComp<CompAssignableToPawn_Cage>();
				if(comp != null)
				{
					if(cage.GetAssignedPawns().Contains(pawn) && comp.HasFreeSlot &&
						(!onlyIfInside || cage.OccupiedRect().Contains(pawn.Position)))
					{
						return cage;
					}
				}
			}
			return null;
		}

		private Area_Cage area;
		public Area_Cage Area
		{
			get => this.area;
			set
			{
				this.Area = value;
			}
		}
		public void ExposeData()
		{
			Scribe_References.Look(ref this.area, "area", false);
		}
		public override void Initialize(CompProperties props)
		{
			base.Initialize(props);
			if(this.parent is Building_Bed cage)
			{
				cage.ForPrisoners = true;
			}
		}
		public override void PostDeSpawn(Map map)
		{
			base.PostDeSpawn(map);
			this.area.valid = false;
			this.area.Delete();
		}
		public override void PostSpawnSetup(bool respawningAfterLoad)
		{
			base.PostSpawnSetup(respawningAfterLoad);
			var bed = this.parent;
			var areaManager = bed.Map.areaManager;
			if(this.Area is null)
			{
				this.Area = new Area_Cage(areaManager);
				areaManager.AllAreas.Add(this.Area);
			}
			var rect = bed.OccupiedRect();
			foreach(var c in bed.Map.AllCells)
			{
				this.Area[c] = rect.Contains(c);
			}
		}
		public override IEnumerable<Pawn> AssigningCandidates
		{
			get
			{
				var bed = this.parent as Building_Bed;
				return !(bed?.Spawned is true)
					? Enumerable.Empty<Pawn>()
					: this.parent.Map.mapPawns.AllPawns.FindAll(pawn =>
						pawn.BodySize <= this.parent.def.building.bed_maxBodySize &&
						pawn.AnimalOrWildMan() == this.parent.def.building.bed_humanlike &&
						pawn.Faction == bed.Faction ^ bed.ForPrisoners);
			}
		}
		protected override bool ShouldShowAssignmentGizmo() => true;
		public override void TryAssignPawn(Pawn pawn)
		{
			if(pawn.ownership == null)
			{
				pawn.ownership = new Pawn_Ownership(pawn);
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
			return !pawn.Map.mapPawns.AllPawnsSpawned.Any(p => p.Downed && !p.InBed());
		}
		public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
		{
			return t is Pawn target && target.Downed && !target.InBed() &&
				CompAssignableToPawn_Cage.HasFreeCagesFor(target) &&
				CompAssignableToPawn_Cage.FindCageFor(target, true) is null &&
				pawn.CanReserve(target, ignoreOtherReservations: forced) && !GenAI.EnemyIsNear(target, 40f);
		}

		public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
		{
			if(t is Pawn victim)
			{
				var cage = CompAssignableToPawn_Cage.FindCageFor(victim);
				if(cage is null)
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
		protected override IEnumerable<Toil> MakeNewToils()
		{
			this.FailOnDestroyedOrNull(TargetIndex.A).
				FailOnDestroyedOrNull(TargetIndex.B).
				FailOnAggroMentalStateAndHostile(TargetIndex.A).
				FailOn(delegate ()
			{
				if(!this.DropBed.OwnersForReading.Contains(this.Takee))
				{
					return true;
				}
				if(this.job.def.makeTargetPrisoner)
				{
					if(!this.DropBed.ForPrisoners)
					{
						return true;
					}
				}
				else if(this.DropBed.ForPrisoners != this.Takee.IsPrisoner)
				{
					return true;
				}
				return false;
			});
			//yield return Toils_Bed.ClaimBedIfNonMedical(TargetIndex.B, TargetIndex.A);
			this.AddFinishAction(delegate
			{
				var cage = this.DropBed;
				var target = this.Takee;
				if(target.ownership.OwnedBed == cage && this.pawn.Position == cage.InteractionCell)
				{
					if(this.job.def.makeTargetPrisoner && !target.AnimalOrWildMan())
					{
						var lord = target.GetLord();
						if(lord != null)
						{
							lord.Notify_PawnAttemptArrested(target);
						}
						GenClamor.DoClamor(target, 10f, ClamorDefOf.Harm);
						if(!target.IsPrisoner)
						{
							QuestUtility.SendQuestTargetSignals(target.questTags, "Arrested", target.Named("SUBJECT"));
						}
						if(!target.CheckAcceptArrest(this.pawn))
						{
							this.pawn.jobs.EndCurrentJob(JobCondition.Incompletable, true, true);
						}
					}
					var position = new IntVec3(cage.InteractionCell.ToVector3()).ClampInsideRect(cage.OccupiedRect());
					this.pawn.carryTracker.TryDropCarriedThing(position, ThingPlaceMode.Direct, out var thing);
					if(!cage.Destroyed && cage.OwnersForReading.Contains(target))
					{
						target.jobs.Notify_TuckedIntoBed(cage);
						target.mindState.Notify_TuckedIntoBed();
						var comp = cage.GetComp<CompAssignableToPawn_Cage>();
						if(comp != null)
						{
							target.playerSettings.AreaRestriction = comp.Area;
						}
					}
					if(target.IsPrisonerOfColony)
					{
						LessonAutoActivator.TeachOpportunity(ConceptDefOf.PrisonerTab, this.Takee, OpportunityType.GoodToKnow);
					}
					target.mindState.duty = new PawnDuty(DefDatabase<DutyDef>.GetNamed("ZzZomboRW_AnimalCage_BeingHelpCaptive"),
						cage);
				}
			});
			yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch).
				FailOnDespawnedNullOrForbidden(TargetIndex.A).
				FailOnDespawnedNullOrForbidden(TargetIndex.B).
				//FailOn(() => this.job.def == JobDefOf.Arrest && !this.Takee.CanBeArrestedBy(this.pawn)).
				FailOn(() => !this.pawn.CanReach(this.DropBed.InteractionCell, PathEndMode.OnCell, Danger.Deadly,
					false, TraverseMode.ByPawn)).
				FailOn(() => !this.Takee.Downed).
				FailOnSomeonePhysicallyInteracting(TargetIndex.A);
			var toil = Toils_Haul.StartCarryThing(TargetIndex.A).
				FailOnNonMedicalBedNotOwned(TargetIndex.B, TargetIndex.A);
			toil.AddPreInitAction(new Action(() =>
			{
				var target = this.Takee;
				if(target.playerSettings == null)
				{
					target.playerSettings = new Pawn_PlayerSettings(target);
				}
				if(this.job.def.makeTargetPrisoner && !target.AnimalOrWildMan())
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
			yield return Toils_Reserve.Release(TargetIndex.B);
			yield break;
		}

		[StaticConstructorOnStartup]
		internal static class HarmonyHelper
		{
			static HarmonyHelper()
			{
				var harmony = new Harmony($"ZzZomboRW.{MOD.NAME}");
				harmony.PatchAll();
			}

			[HarmonyPatch(typeof(Pawn_PlayerSettings), nameof(Pawn_PlayerSettings.RespectsAllowedArea))]
			public static class Pawn_PlayerSettings_RespectsAllowedAreaPatch
			{
				private static void Postfix(ref bool __result, Pawn_PlayerSettings __instance)
				{
					if(__result)
					{
						return;
					}
					var pawn = new Traverse(__instance).Field<Pawn>("pawn").Value;
					var cage = CompAssignableToPawn_Cage.FindCageFor(pawn, true);
					__result = cage is null;
				}
			}

			//[HarmonyPatch(typeof(RestUtility), nameof(RestUtility.CurrentBed))]
			//public static class RestUtility_CurrentBedPatch
			//{
			//	private static void Postfix(ref Building_Bed __result, Pawn __instance)
			//	{
			//		if(!(__result is null))
			//		{
			//			return;
			//		}
			//		__result = CompAssignableToPawn_Cage.FindCageFor(__instance, true);
			//	}
			//}
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
			return pawn.Map.GetComponent<MapComponent_Cage>().cages.Count < 1;
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
	public class ThinkNode_ConditionalInsideCage: ThinkNode_Conditional
	{
		public override ThinkResult TryIssueJobPackage(Pawn pawn, JobIssueParams jobParams)
		{
			ThinkResult result;
			try
			{
				if(pawn.mindState.duty?.focus.HasThing is true)
				{
					if(this.Satisfied(pawn) ^ this.invert)
					{
						var area = pawn.mindState.duty.focus.Thing.OccupiedRect();
						pawn.mindState.maxDistToSquadFlag = Math.Max(area.Width, area.Height);
					}
					else
					{
						pawn.mindState.maxDistToSquadFlag = -1;
					}
				}
				result = base.TryIssueJobPackage(pawn, jobParams);
			}
			finally
			{
				pawn.mindState.maxDistToSquadFlag = -1f;
			}
			return result;
		}
		protected override bool Satisfied(Pawn pawn)
		{
			return pawn.mindState.duty?.focus.HasThing is true && pawn.Position.IsInside(pawn.mindState.duty.focus.Thing);
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
