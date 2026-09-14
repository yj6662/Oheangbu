using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;

namespace Oheangbu.EditorTools
{
    /// <summary>Read-only final bone matrices for source-bound Blender deformation review.</summary>
    internal sealed class DosaV2PoseTrajectoryWriter : IDisposable
    {
        [Serializable] private sealed class Header
        {
            public string format = "DOSA_V2_BONE_MATRICES_LE_V1";
            public string scope = "Actual final world-space bone matrices at the existing post-skin capture stage. Diagnostic poses only, no animation asset. Each fixed-size record is little-endian int32 ordinal followed by world then near bone matrices,16 float32 row-major values per bone. Bone names match actual imported renderer palettes.";
            public string[] worldBoneNames, nearBoneNames;
            public string worldModelSha256, nearModelSha256, binarySha256;
            public int recordBytes, records;
            public bool complete;
        }
        private readonly Header _header;
        private readonly string _path, _headerPath;
        private readonly Transform[] _world, _near;
        private BinaryWriter _writer;

        internal DosaV2PoseTrajectoryWriter(string directory, Animator world, Animator near)
        {
            _world = Bones(world); _near = Bones(near);
            _path = Path.Combine(directory, "actual-bone-trajectory.bin");
            _headerPath = Path.Combine(directory, "actual-bone-trajectory.json");
            _header = new Header { worldBoneNames = _world.Select(b => b.name).ToArray(), nearBoneNames = _near.Select(b => b.name).ToArray(),
                recordBytes = 4 + 64 * (_world.Length + _near.Length),
                worldModelSha256 = Sha(DosaV2PlayerBuilder.WorldModel), nearModelSha256 = Sha(DosaV2PlayerBuilder.ArmsModel) };
            _writer = new BinaryWriter(new FileStream(_path, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
            File.WriteAllText(_headerPath, JsonUtility.ToJson(_header, true));
        }
        private static Transform[] Bones(Animator animator)
        {
            var bones = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .SelectMany(s => s.bones).Distinct().OrderBy(b => b.name, StringComparer.Ordinal).ToArray();
            if (bones.Length == 0 || bones.Any(b => b == null) || bones.Select(b => b.name).Distinct().Count() != bones.Length)
                throw new InvalidOperationException("Unambiguous actual imported bone palettes are required.");
            return bones;
        }
        internal void Append(int ordinal)
        {
            if (_writer == null) throw new ObjectDisposedException(nameof(DosaV2PoseTrajectoryWriter));
            _writer.Write(ordinal); Write(_world); Write(_near); _header.records++;
        }
        private void Write(Transform[] bones)
        {
            foreach (var bone in bones)
            {
                Matrix4x4 matrix = bone.localToWorldMatrix;
                for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++)
                {
                    float value = matrix[r, c];
                    if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidOperationException("Nonfinite actual bone matrix.");
                    _writer.Write(value);
                }
            }
        }
        internal void Complete(bool completed)
        {
            if (_writer == null) return;
            _writer.Flush(); _writer.Dispose(); _writer = null;
            _header.complete = completed; _header.binarySha256 = Sha(_path);
            if (new FileInfo(_path).Length != (long)_header.records * _header.recordBytes)
                throw new InvalidDataException("Trajectory record size mismatch.");
            File.WriteAllText(_headerPath, JsonUtility.ToJson(_header, true));
        }
        public void Dispose() => Complete(false);
        private static string Sha(string path)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
    }
}
