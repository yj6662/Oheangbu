# Compression map draft

`WorldMacroCompressionMapSO.cs.tmp` contains the serializable math map and its optional ScriptableObject wrapper. `WorldMacroCompressionMapTests.cs.tmp` contains pure NUnit checks. Both remain outside Unity Assets and have `.tmp` extensions; no Unity import or asset refresh was performed.

## API

```csharp
using Oheangbu.Data.World;

bool built = WorldMacroCompressionMap.TryBuild(
    sourceBounds,
    new Vector2(-2000, -3000),
    new Vector2(2000, 3000),
    protectedX,
    protectedZ,
    shoulderWidth: 100.0,
    minimumDerivative: 0.025,
    out WorldMacroCompressionMap mapping,
    out string reason);

// On success only:
Vector3 destination = mapping.Map(sourcePosition);
Vector3 original = mapping.Inverse(destination);
double eastWestDerivative = mapping.XAxis.Derivative(sourcePosition.x);
double northSouthDerivative = mapping.ZAxis.Derivative(sourcePosition.z);
// Optional asset: compressionMapAsset.Mapping = mapping;
```

Every protected content rectangle contributes `[minX,maxX]` to `protectedX` and `[minZ,maxZ]` to `protectedZ`. Add the complete footprint including clearance before building. All points in that rectangle then undergo the same X/Z translation; Y is unchanged. Because this is separable, projections can protect other terrain sharing an axis interval. The builder reports failure if their union prevents the requested target size.

Source/target bounds and format version are serialized. Each axis stores merged protection intervals plus exact analytic segment endpoints and endpoint derivatives. The derivative uses cubic smoothstep, and its exact integral gives the mapped position. All segment joins have continuous position, first derivative, and second derivative. Inverse uses a constant-segment formula or a bracketed 60-step solve on a shoulder.

The positive base derivative is solved once per axis. If `P` is total protected length, `S` is half the total shoulder lengths, `L` is source span, and `m` is the configured derivative floor, the minimum feasible target span is `P + S + m * (L - P - S)`. A smaller target is rejected with these values in the error reason. No coordinate clamp, zero-width collapse, automatic loss of protection, or fold repair occurs.

Overlapping and touching protected intervals merge. A gap between two protected intervals must have room for two complete shoulders; an edge gap needs one. Short gaps are rejected so the authoring layer can explicitly merge nearby content clusters or choose a smaller shoulder width. Intervals outside source bounds and nonfinite values are rejected. Coordinates outside source bounds extend the endpoint tangent and remain invertible; they are not silently clamped to the map edge.

Use `XAxis/ZAxis` double methods for terrain baking and numeric validation. `Map/Inverse(Vector3)` round to Unity float precision, and the test tolerance for a kilometre-scale full-world round trip is 2 mm. Object rotations/scales, terrain height resampling, normals, navigation, route geometry, and asset writes belong to the caller. If using inverse terrain sampling, preserve world-space Y and sample source terrain at `mapping.Inverse(targetXZ)`.

## Verification

Executed with the installed Unity 6000.3.9f1 `UnityEngine.CoreModule.dll` and cached NUnit DLL, using .NET outside Unity:

```powershell
dotnet run --project Tools/WorldCompact/TransformVerification/TransformVerification.csproj --configuration Release
```

Result: **14 passed, 0 failed**. Checks cover exact target bounds; overlapping/touching protection; protected distances; 24,001 forward/inverse double samples; 40,000 positive-derivative and monotonicity samples; smooth joins; unclamped extrapolation; infeasible protection, shoulders and derivative floor; full-axis protection; invalid/nonfinite input; an 8×12km to 4×6km map with 2,000 protected rectangle and 5,000 world samples; and atomic failure when the second axis cannot be built.

The verification project uses machine-local Unity/NUnit paths and is a development runner, not a Unity package. The draft NUnit file can later be placed in an appropriate test assembly if requested.
