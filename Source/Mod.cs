using UnityEngine;
using HarmonyLib;
using Verse;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse.AI;

internal static class MOD
{
	public const string NAME = "MOD";
}

namespace ZzZomboRW
{
	public class CompAssignableToPawn_Cage: CompAssignableToPawn
	{
		public static readonly Dictionary<Map, List<Building>> cache = new Dictionary<Map, List<Building>>();
		public static bool HasFreeCagesFor(Pawn target) => FindCageFor(target) != null
			;
		public static Building FindCageFor(Pawn pawn)
		{
			foreach(var cage in cache[pawn.Map])
			{
				var comp = cage.GetComp<CompAssignableToPawn_Cage>();
				if(comp != null)
				{
					if(cage.GetAssignedPawns().Contains(pawn) && comp.HasFreeSlot)
					{
						return cage;
					}
				}
			}
			return null;
		}
		public override IEnumerable<Pawn> AssigningCandidates
		{
			get
			{
				var bed = this.parent as Building_Bed;
				return !(bed?.Spawned ?? false)
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
			if(!(t is Pawn target) || !target.Downed || target.InBed() ||
				!CompAssignableToPawn_Cage.HasFreeCagesFor(target) ||
				!pawn.CanReserve(target, 1, -1, null, forced) || GenAI.EnemyIsNear(target, 40f))
			{
				return false;
			}
			var result = CompAssignableToPawn_Cage.FindCageFor(pawn);
			return result != null;
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
				Job job = JobMaker.MakeJob(JobDefOf.Rescue, victim, cage);
				job.count = 1;
				return job;
			}
			return null;
		}
	}
	public class CompProperties_X: CompProperties
	{
		public bool enabled = true;
		public int data = -1;
		public CompProperties_X()
		{
			this.compClass = typeof(CompX);
			Log.Message($"[{this.GetType().FullName}] Initialized:\n" +
				$"\tCurrent ammo: {this.data};\n" +
				$"\tEnabled: {this.enabled}.");
		}

	}
	public class CompX: ThingComp
	{
		public CompProperties_X Props => (CompProperties_X)this.props;
		public bool Enabled => this.Props.enabled;
		public override void Initialize(CompProperties props)
		{
			base.Initialize(props);
			Log.Message($"[{this.GetType().FullName}] Initialized for {this.parent}:\n" +
				$"\tData: {this.Props.data};\n" +
				$"\tEnabled: {this.Props.enabled}.");
		}

		public override void PostExposeData()
		{
			Scribe_Values.Look(ref this.Props.data, "data", 1, false);
			Scribe_Values.Look(ref this.Props.enabled, "enabled", true, false);
		}

		[StaticConstructorOnStartup]
		internal static class HarmonyHelper
		{
			static HarmonyHelper()
			{
				Harmony harmony = new Harmony($"ZzZomboRW.{MOD.NAME}");
				harmony.PatchAll();
			}

			[HarmonyPatch(typeof(object), nameof(object.Equals))]
			public static class Class_MethodPatch
			{
				private static void Postfix(ref object __result, object __instance)
				{
				}
			}
		}
	}
}
