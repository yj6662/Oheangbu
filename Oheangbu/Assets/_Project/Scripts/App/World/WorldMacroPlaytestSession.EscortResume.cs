using System;
using Oheangbu.App.Demo;
using UnityEngine;
using UnityEngine.AI;

namespace Oheangbu.App.World
{
    public sealed partial class WorldMacroPlaytestSession
    {
        public bool DemoEscortReady => ready && isActiveAndEnabled;
        const float MaximumEscortResumeDistance = 32;
        static bool ActiveEscort(WorldMacroProgress source) => source?.escort != null &&
            source.escort.Stage >= DemoEscortStage.Escorting && source.escort.Stage < DemoEscortStage.Delivered;

        // Called after normal ledger sampling, before SaveNow serializes. This records an actual
        // companion position in the same file as the player's safe feet, without changing a quest stage.
        WorldMacroProgress CreateDemoEscortSaveSnapshot(WorldMacroProgress source)
        {
            if (!DemoCampaignActive || !ActiveEscort(source) || Walker == null || Walker.Body == null || Walker.Seated ||
                DemoEscortSeat != null && DemoEscortSeat.Occupied || DemoEscortCompanion == null || DemoEscortCargo == null ||
                !DemoEscortCompanion.gameObject.activeInHierarchy || !DemoEscortCargo.gameObject.activeInHierarchy ||
                DemoEscortCompanion.gameObject.scene != gameObject.scene || DemoEscortCargo.gameObject.scene != gameObject.scene ||
                DemoEscortSeat != null && DemoEscortSeat.Vehicle != null && DemoEscortCompanion.IsChildOf(DemoEscortSeat.Vehicle.transform) ||
                !TryEscortResumeCompanionFeet(DemoEscortCompanion.position, out var feet)) return source;
            return TryPrepareDemoEscortWalkingSnapshot(source, feet, DemoEscortCargo.position, out var candidate) ? candidate : source;
        }

        static bool TryPrepareDemoEscortWalkingSnapshot(WorldMacroProgress source, Vector3 companion, Vector3 cargo, out WorldMacroProgress candidate)
        {
            candidate = null;
            if (!WorldMacroProgress.Valid(source) || !ActiveEscort(source) || source.escort.CompanionMode == DemoEscortCompanionMode.Riding ||
                !DemoEscortState.Finite(source.ledger.position) || !DemoEscortState.Finite(companion) || !DemoEscortState.Finite(cargo) ||
                Vector3.Distance(source.ledger.position, companion) > MaximumEscortResumeDistance || Vector3.Distance(companion, cargo) > 2) return false;
            candidate = JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(source));
            candidate.escort.CompanionFeet = companion;
            return WorldMacroProgress.Valid(candidate);
        }

        // Start invokes this after loading/migration and before RepairProgress/Teleport. A failed
        // normalization save leaves the original loaded state untouched and startup must stop.
        bool TryNormalizeDemoEscortReload(out string error)
        {
            error = null;
            if (!DemoCampaignActive || !ActiveEscort(Progress)) return true;
            if (DemoEscortCompanion == null || DemoEscortCargo == null || Walker == null || Walker.Body == null ||
                DemoEscortCompanion.gameObject.scene != gameObject.scene || DemoEscortCargo.gameObject.scene != gameObject.scene)
            { error = "호송 재개에 필요한 왕소·화물 참조가 없다."; return false; }
            var saved = Progress;
            Vector3 player = default, companion = default;
            bool walking = saved.terrainRevision == Content.TerrainRevision && saved.escort.CompanionMode != DemoEscortCompanionMode.Riding &&
                saved.ledger.hasPosition && TrySafeFeet(saved.ledger.position, out player) &&
                TryEscortResumeCompanionFeet(saved.escort.CompanionFeet, out companion) &&
                Vector3.Distance(player, companion) <= MaximumEscortResumeDistance && Vector3.Distance(player, companion) >= .75f;
            string checkpointId = saved.escort.CheckpointId; float yaw = saved.ledger.yaw;
            if (!walking)
            {
                if (!WorldMacroCheckpointRules.TryResolve(Content, saved, checkpointId, out var checkpoint) || !TrySafeFeet(checkpoint.Feet, out player) ||
                    !TryEscortResumeCompanionNear(player, checkpoint.Yaw, out companion))
                { error = "최근 호송 쉼터에서 플레이어와 왕소가 함께 설 안전한 위치를 확인할 수 없다."; return false; }
                yaw = checkpoint.Yaw;
            }
            if (!TryPrepareDemoEscortResume(saved, walking, player, companion, checkpointId, yaw, Content.TerrainRevision, out var candidate))
            { error = "호송 재개 저장 후보를 확인할 수 없다."; return false; }
            string snapshot = JsonUtility.ToJson(candidate);
            if (snapshot == JsonUtility.ToJson(saved)) return true;
            if (!TryPersistDemoEscortCandidate(candidate, store.Save, out var accepted, out error)) { SaveError = error; return false; }
            Progress = accepted; lastSuccessfulSnapshot = snapshot; SaveError = null; return true;
        }

        static bool TryPrepareDemoEscortResume(WorldMacroProgress source, bool walkingVerified, Vector3 player, Vector3 companion,
            string checkpointId, float yaw, string terrainRevision, out WorldMacroProgress candidate)
        {
            candidate = null;
            if (!WorldMacroProgress.Valid(source) || !ActiveEscort(source) || !DemoEscortState.Finite(player) || !DemoEscortState.Finite(companion) ||
                !float.IsFinite(yaw) || string.IsNullOrWhiteSpace(terrainRevision) ||
                Vector3.Distance(player, companion) < .75f || Vector3.Distance(player, companion) > MaximumEscortResumeDistance ||
                walkingVerified && (source.escort.CompanionMode == DemoEscortCompanionMode.Riding ||
                    source.terrainRevision != terrainRevision ||
                    Vector3.Distance(source.ledger.position, player) > .75f || Vector3.Distance(source.escort.CompanionFeet, companion) > .75f) ||
                !walkingVerified && (string.IsNullOrWhiteSpace(checkpointId) || checkpointId != source.escort.CheckpointId)) return false;
            candidate = JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(source));
            candidate.ledger.position = player; candidate.ledger.hasPosition = true; candidate.ledger.yaw = Mathf.Repeat(yaw, 360);
            candidate.terrainRevision = terrainRevision;
            candidate.escort.CompanionMode = DemoEscortCompanionMode.Waiting; candidate.escort.CompanionFeet = companion;
            if (!walkingVerified)
            {
                candidate.ledger.checkpoint = checkpointId; candidate.ledger.checkpointPosition = player;
                candidate.escort.CheckpointFeet = player; candidate.escort.CheckpointYaw = Mathf.Repeat(yaw, 360);
            }
            // This is transport restoration, not a campaign command or a rest: no reward, HP/ink,
            // consumed virtue, drop, encounter, inspection, or LastEvidenceId is changed.
            return WorldMacroProgress.Valid(candidate);
        }

        bool TryEscortResumeCompanionNear(Vector3 player, float yaw, out Vector3 feet)
        {
            var rotation = Quaternion.Euler(0, yaw, 0);
            for (int i = 0; i < 8; i++)
            {
                Vector3 offset = rotation * Quaternion.Euler(0, i * 45, 0) * Vector3.right * 1.5f;
                if (TryEscortResumeCompanionFeet(player + offset, out feet) && Vector3.Distance(player, feet) >= .75f &&
                    Vector3.Distance(player, feet) <= 3) return true;
            }
            feet = default; return false;
        }
        bool TryEscortResumeCompanionFeet(Vector3 proposed, out Vector3 feet)
        {
            feet = default;
            if (!DemoEscortState.Finite(proposed) || proposed.x < PreviewSheetBoundsMin.x || proposed.x > PreviewSheetBoundsMax.x ||
                proposed.z < PreviewSheetBoundsMin.y || proposed.z > PreviewSheetBoundsMax.y ||
                !NavMesh.SamplePosition(proposed, out var navigation, .6f, NavMesh.AllAreas)) return false;
            foreach (var hit in Physics.RaycastAll(navigation.position + Vector3.up * 1.5f, Vector3.down, 4, 1, QueryTriggerInteraction.Ignore))
            {
                if (hit.normal.y < Mathf.Cos(Walker.Body.slopeLimit * Mathf.Deg2Rad) || EscortResumeOwnedActor(hit.transform)) continue;
                Vector3 point = hit.point + Vector3.up * .05f;
                if (Vector3.Distance(point, proposed) > .75f) continue;
                bool blocked = false;
                foreach (var obstacle in Physics.OverlapCapsule(point + Vector3.up * .32f, point + Vector3.up * 1.5f, .3f, ~0, QueryTriggerInteraction.Ignore))
                    if (!EscortResumeOwnedActor(obstacle.transform)) { blocked = true; break; }
                if (blocked) continue;
                bool supported = true;
                foreach (var offset in new[] { Vector3.right * .28f, Vector3.left * .28f, Vector3.forward * .28f, Vector3.back * .28f })
                    supported &= Physics.Raycast(point + offset + Vector3.up * .3f, Vector3.down, .65f, 1, QueryTriggerInteraction.Ignore);
                if (supported) { feet = point; return true; }
            }
            return false;
        }
        bool EscortResumeOwnedActor(Transform value) => value.IsChildOf(Walker.Body.transform) ||
            DemoEscortCompanion != null && value.IsChildOf(DemoEscortCompanion) || DemoEscortCargo != null && value.IsChildOf(DemoEscortCargo);
    }
}
