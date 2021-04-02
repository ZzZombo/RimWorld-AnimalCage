using UnityEngine;
using HarmonyLib;
using Verse;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse.AI;
using Verse.AI.Group;
using System;

internal static class MOD
{
	public const string NAME = "MOD";
}

namespace ZzZomboRW
{
	public class Building_Cage: Building_Bed
	{
		private bool changedTerrain = false;
		public override void SpawnSetup(Map map, bool respawningAfterLoad)
		{
			base.SpawnSetup(map, respawningAfterLoad);
			if(!this.changedTerrain)
			{
				foreach(var c in this.OccupiedRect())
				{
					base.Map.terrainGrid.SetTerrain(c, this.def.building.naturalTerrain ?? TerrainDefOf.WoodPlankFloor);
				}
			}
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
			"Cage_",
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
		public static readonly Dictionary<Map, List<Building>> cache = new Dictionary<Map, List<Building>>();

		public static bool HasFreeCagesFor(Pawn target) => FindCageFor(target) != null;
		public static Building FindCageFor(Pawn pawn, bool onlyIfInside = true)
		{
			foreach(var cage in cache[pawn.Map])
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
				Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("ZzZomboRW_AnimalCage_Capture"), victim, cage);
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
			base.AddFinishAction(delegate
			{
				var cage = this.DropBed;
				var target = this.Takee;
				if(target.ownership.OwnedBed == cage && this.pawn.Position == cage.InteractionCell)
				{
					var comp = cage.GetComp<CompAssignableToPawn_Cage>();
					if(this.job.def.makeTargetPrisoner)
					{
						Lord lord = target.GetLord();
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
					this.pawn.carryTracker.TryDropCarriedThing(position, ThingPlaceMode.Direct, out var thing, null);
					if(!cage.Destroyed && (cage.OwnersForReading.Contains(target)))
					{
						target.jobs.Notify_TuckedIntoBed(cage);
						target.mindState.Notify_TuckedIntoBed();
						if(comp != null)
						{
							target.playerSettings.AreaRestriction = comp.Area;
						}
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
				//FailOn(() => this.job.def == JobDefOf.Arrest && !this.Takee.CanBeArrestedBy(this.pawn)).
				FailOn(() => !this.pawn.CanReach(this.DropBed.InteractionCell, PathEndMode.OnCell, Danger.Deadly,
					false, TraverseMode.ByPawn)).
				FailOn(() => !this.Takee.Downed).
				FailOnSomeonePhysicallyInteracting(TargetIndex.A);
			Toil toil = Toils_Haul.StartCarryThing(TargetIndex.A, false, false, false).
				FailOnNonMedicalBedNotOwned(TargetIndex.B, TargetIndex.A);
			toil.AddPreInitAction(new Action(() =>
			{
				if(this.Takee.playerSettings == null)
				{
					this.Takee.playerSettings = new Pawn_PlayerSettings(this.Takee);
				}
				if(this.job.def.makeTargetPrisoner)
				{
					if(this.Takee.guest is null)
					{
						this.Takee.guest = new Pawn_GuestTracker(this.Takee);
					}
					if(this.Takee.guest.Released)
					{
						this.Takee.guest.Released = false;
						this.Takee.guest.interactionMode = PrisonerInteractionModeDefOf.ReduceResistance;
						GenGuest.RemoveHealthyPrisonerReleasedThoughts(this.Takee);
					}
					if(!this.Takee.IsPrisonerOfColony)
					{
						this.Takee.guest.CapturedBy(Faction.OfPlayer, this.pawn);
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
				Harmony harmony = new Harmony($"ZzZomboRW.{MOD.NAME}");
				harmony.PatchAll();
			}

			[HarmonyPatch(typeof(Pawn_PlayerSettings), nameof(Pawn_PlayerSettings.RespectsAllowedArea))]
			public static class Pawn_PlayerSettings_RespectsAllowedAreaPatch
			{
				private static void Postfix(ref bool __result, Pawn_PlayerSettings __instance)
				{
					var pawn = new Traverse(__instance).Field<Pawn>("pawn").Value;
					var cage = CompAssignableToPawn_Cage.FindCageFor(pawn, true);
					if(cage != null)
					{
						__result = true;
					}
				}
			}
			//[HarmonyPatch(typeof(AreaManager), nameof(AreaManager.AddStartingAreas))]
			//public static class AreaManager_AddStartingAreasPatch
			//{
			//	private static void Postfix(AreaManager __instance)
			//	{
			//		__instance.AllAreas.Add(new Area_Cage(__instance));
			//	}
			//}
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
