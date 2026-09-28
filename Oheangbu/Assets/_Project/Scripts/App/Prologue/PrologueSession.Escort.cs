using System;
using Oheangbu.App.Demo;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.Prologue
{
    public sealed partial class PrologueSession
    {
        public Transform EscortCompanion, EscortCargo;

        // Contract acceptance only. Starting the journey still requires the vehicle/seat bridge.
        bool TryInteractJourneyEscort(PrologueContentSO.Point point)
        {
            if (point.Id != "wangso_w1") return false;
            bool Present(Transform actor) => actor != null && actor.gameObject.activeInHierarchy &&
                actor.gameObject.scene == gameObject.scene;
            if (!Present(EscortCompanion) || !Present(EscortCargo) ||
                Vector3.Distance(EscortCargo.position, point.Position) > DemoEscortRules.MaximumCompanionDistance)
            {
                Resolve(InteractionResult.Waiting);
                return true;
            }
            var expected = Progress.escort ?? new DemoEscortState();
            var proof = new DemoEscortEvidence
            {
                Command = DemoEscortCommand.AcceptContract, ExpectedRevision = expected.Revision,
                EventId = Guid.NewGuid().ToString("N"), InteractionId = point.Id,
                SessionVerified = true, CompanionPresent = true, CargoPresent = true,
                PlayerFeet = Player.position, PointFeet = point.Position, CompanionFeet = EscortCompanion.position
            };
            var status = DemoEscortRules.TryPrepare(expected, proof.Command, proof, out var next, out _);
            if (status != DemoEscortStatus.Prepared)
            {
                Resolve(InteractionResult.Waiting);
                return true;
            }
            var candidate = CopyProgress();
            candidate.escort = next;
            PrologueProgressStore.Complete(candidate, point.Id, 0);
            Resolve(TryCommit(candidate) ? InteractionResult.Success : InteractionResult.SaveFailed);
            return true;
        }
    }
}
