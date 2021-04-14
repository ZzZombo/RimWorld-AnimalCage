using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ZzZomboRW
{
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
		public override void SortAssignedPawns()
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
		public override string GetAssignmentGizmoLabel() => "ZzZomboRW_AnimalCage_AssignToCageLabel".Translate();
		public override string GetAssignmentGizmoDesc() => "ZzZomboRW_AnimalCage_AssignToCageDesc".Translate();
	}
}
