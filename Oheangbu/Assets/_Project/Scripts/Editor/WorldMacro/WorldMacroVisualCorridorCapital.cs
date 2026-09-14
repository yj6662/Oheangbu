using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>
    /// Asset-first exterior pass for the SouthPost -> SouthGate -> Hwanggyeong corridor.
    /// It owns only the 03_CapitalExterior group and scene-instance visibility in the
    /// reviewed capital bounds. Geography, content positions, and Palace landmark assets stay intact.
    /// </summary>
    public static partial class WorldMacroVisualCorridorAuthoring
    {
        const string CapitalGroup = "03_CapitalExterior";
        const string CapitalComplete = "CapitalExterior_v1_COMPLETE";
        const string CapitalRefineComplete = "CapitalExterior_v2_REFINE_COMPLETE";
        const string CapitalPortalComplete = "CapitalExterior_v3_PORTAL_COMPLETE";
        const string CapitalPortalCorrectComplete = "CapitalExterior_v4_PORTAL_SOUTH_COMPLETE";
        const string CapitalPalaceAccessComplete = "CapitalExterior_v5_PALACE_ACCESS_COMPLETE";

        const string CapitalWall = "Assets/HwaseongForteressGate/Prefabs/SM_CW.prefab";
        const string CapitalArch = "Assets/HwaseongForteressGate/Prefabs/SM_FortificationArch.prefab";
        const string CapitalFloor = "Assets/HwaseongForteressGate/Prefabs/SM_CW_Floor.prefab";
        const string CapitalStoneCourse = "Assets/HwaseongForteressGate/Prefabs/SM_CW_Stone.prefab";
        const string CapitalGateColumn = "Assets/HwaseongForteressGate/Prefabs/SM_B_GateHouse_Column.prefab";
        const string CapitalGateCrossbeam = "Assets/HwaseongForteressGate/Prefabs/SM_B_GateHouse_Crossbeam.prefab";
        const string CapitalGateRafter = "Assets/HwaseongForteressGate/Prefabs/SM_B_GateHouse_Rafter.prefab";
        const string CapitalGateRoof = "Assets/HwaseongForteressGate/Prefabs/SM_B_GateHouse_Roof_001.prefab";
        const string CapitalBrickPaving = "Assets/HwaseongForteressGate/Prefabs/SM_BarbicanFloor_001.prefab";
        const string CapitalStraightWallLong = "Assets/HwaseongHaenggung/Prefabs/SM_StraightStronewall_1.prefab";
        const string CapitalStraightWallShort = "Assets/HwaseongHaenggung/Prefabs/SM_StraightStronewall_2.prefab";
        const string CapitalPavilion = "Assets/HwaseongHaenggung/Prefabs/SM_Naeposa.prefab";
        const string CapitalHouse = "Assets/House_2/house2 .fbx";
        const string CapitalWoodenBox = "Assets/HwaseongHaenggung/Prefabs/SM_M_WoodenBox.prefab";
        const string CapitalWoodLog = "Assets/HwaseongHaenggung/Prefabs/SM_M_WoodLog.prefab";
        const string CapitalRiceSack = "Assets/KoreanTraditionalFestival/Prefabs/SM_SackOfRice.prefab";
        const string CapitalBasket = "Assets/KoreanTraditionalFestival/Prefabs/SM_Basket.prefab";
        const string CapitalPot = "Assets/Korea_TreasureProps/Prefabs/SM_052_Pot.prefab";

        static readonly Vector3 SouthGateAnchor = new Vector3(-270f, 99.664f, -1260f);
        static readonly Vector3 HwanggyeongAnchor = new Vector3(-300f, 72.839f, -630f);
        static readonly Vector3 PalaceAnchor = new Vector3(-300f, 76.868f, -788.138f);

        static readonly string[] CapitalSources =
        {
            CapitalWall, CapitalArch, CapitalFloor, CapitalStoneCourse, CapitalGateColumn,
            CapitalGateCrossbeam, CapitalGateRafter, CapitalGateRoof, CapitalBrickPaving,
            CapitalStraightWallLong, CapitalStraightWallShort, CapitalPavilion, CapitalHouse,
            CapitalWoodenBox, CapitalWoodLog, CapitalRiceSack, CapitalBasket, CapitalPot
        };

        static readonly HashSet<string> ReplaceableMassingNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Massing_CityWall", "Massing_Foundation", "Massing_Roof", "Massing_Walls",
            "Gate_Left", "Gate_Right"
        };

        static partial void PlanCapital()
        {
            var capitalIds = new HashSet<string>(new[]
            {
                "capital_approach", "gate_street", "palace_front", "capital_aerial", "gate_close"
            }, StringComparer.Ordinal);
            var views = (Sheet.Views ?? Array.Empty<Oheangbu.Data.World.WorldMacroVisualCorridorSO.View>())
                .Where(v => v != null && !capitalIds.Contains(v.Id))
                .Concat(new[]
                {
                    new Oheangbu.Data.World.WorldMacroVisualCorridorSO.View
                    {
                        Id="capital_approach", Eye=CapitalEye(-215f,-1365f), Target=CapitalGrounded(-270f,-1260f)+Vector3.up*9f
                    },
                    new Oheangbu.Data.World.WorldMacroVisualCorridorSO.View
                    {
                        Id="gate_street", Eye=CapitalEye(-262.875f,-1239.75f), Target=CapitalGrounded(-160f,-970f)+Vector3.up*4f
                    },
                    new Oheangbu.Data.World.WorldMacroVisualCorridorSO.View
                    {
                        Id="palace_front", Eye=CapitalEye(-300f,-700f), Target=PalaceAnchor+Vector3.up*6f
                    },
                    new Oheangbu.Data.World.WorldMacroVisualCorridorSO.View
                    {
                        Id="capital_aerial", Eye=new Vector3(-105f,245f,-1010f), Target=new Vector3(-275f,88f,-920f)
                    },
                    new Oheangbu.Data.World.WorldMacroVisualCorridorSO.View
                    {
                        Id="gate_close", Eye=CapitalEye(-279f,-1238f), Target=CapitalGrounded(-279f,-1276f)+Vector3.up*8f
                    }
                }).ToArray();
            SetViews(views);
        }

        static partial void BuildCapital()
        {
            PlanCapital();
            PreflightCapitalSources();
            RequireNamedAnchor("SouthGate", SouthGateAnchor, 1f);
            RequireNamedAnchor("Hwanggyeong", HwanggyeongAnchor, 1f);
            var palace = RequirePreservedPalace();

            var root = Group(CapitalGroup);
            if (root.Find(CapitalComplete) != null)
            {
                EditorUtility.SetDirty(Sheet);
                AssetDatabase.SaveAssets();
                return;
            }

            BuildSouthGate(root);
            BuildSouthWall(root);
            BuildRepresentativeStreet(root);
            BuildMarketEdges(root);
            BuildCityCentreSilhouette(root);
            BuildPalaceForecourt(root);

            int replaced = ReplaceCapitalMassingInstances();
            HideCoveredPalacePrimitives(palace);
            ValidateOpenGate(root);
            ValidateStreetClearance(root);

            var complete = new GameObject(CapitalComplete);
            complete.transform.SetParent(root, false);
            var replacementAudit = new GameObject("ReplacedMassingRenderers_" + replaced);
            replacementAudit.transform.SetParent(complete.transform, false);
            EditorUtility.SetDirty(Sheet);
            AssetDatabase.SaveAssets();
        }

        public static string RefineCapital()
        {
            PreflightCapitalSources();
            RequirePreservedPalace();
            var corridor = GameObject.Find(RootName);
            if (corridor == null) throw new InvalidOperationException("Install the visual corridor before capital-refine.");
            var root = corridor.transform.Find(CapitalGroup);
            if (root == null || root.Find(CapitalComplete) == null)
                throw new InvalidOperationException("Install the capital exterior before capital-refine.");
            if (root.Find(CapitalRefineComplete) != null) return "Capital exterior refinement already installed.";

            WidenGateAssembly(root,2f,20.4f,15f);
            int buildings = ReplaceCapitalProxyBuildings(root);
            ValidateOpenGate(root);
            ValidateStreetClearance(root);

            var marker = new GameObject(CapitalRefineComplete);
            marker.transform.SetParent(root, false);
            var audit = new GameObject("OwnedCityBuildings_" + buildings);
            audit.transform.SetParent(marker.transform, false);
            EditorUtility.SetDirty(Sheet);
            AssetDatabase.SaveAssets();
            return "Capital refined: widened owned gate and " + buildings + " city proxy houses replaced.";
        }

        public static string WidenCapitalPortal()
        {
            var corridor = GameObject.Find(RootName);
            if (corridor == null) throw new InvalidOperationException("Install the visual corridor before capital-portal.");
            var root = corridor.transform.Find(CapitalGroup);
            if (root == null || root.Find(CapitalRefineComplete) == null)
                throw new InvalidOperationException("Run capital-refine before capital-portal.");
            WidenGateAssembly(root,2.6f,26.6f,20f);
            RestoreCapitalFoundationSurfaces();
            AddGateCloseView();
            ValidateOpenGate(root);
            if (root.Find(CapitalPortalComplete) == null)
            {
                var marker = new GameObject(CapitalPortalComplete);
                marker.transform.SetParent(root,false);
            }
            EditorUtility.SetDirty(Sheet);
            AssetDatabase.SaveAssets();
            return "Capital portal widened to its final owned-arch assembly.";
        }

        public static string CorrectCapitalPortal()
        {
            var corridor = GameObject.Find(RootName);
            if (corridor == null) throw new InvalidOperationException("Install the visual corridor before capital-portal-correct.");
            var root = corridor.transform.Find(CapitalGroup);
            if (root == null || root.Find(CapitalPortalComplete) == null)
                throw new InvalidOperationException("Run capital-portal before capital-portal-correct.");

            // The authored routes turn around on the north plaza instead of passing through the arch.
            // Keep their source geometry intact and place the full gate frontage at the plaza's south edge.
            NormalizeGateAssembly(root,-279f,-1276f);
            AddGateCloseView();
            ValidateOpenGate(root);
            if (root.Find(CapitalPortalCorrectComplete) == null)
            {
                var marker = new GameObject(CapitalPortalCorrectComplete);
                marker.transform.SetParent(root,false);
            }
            EditorUtility.SetDirty(Sheet);
            AssetDatabase.SaveAssets();
            return "Capital portal normalized at the south plaza edge; route sources remain unchanged.";
        }

        [Serializable] sealed class PalaceDeckAudit
        {
            public string id;
            public bool found,hasRenderer,hasBoxCollider,boxColliderEnabled;
            public Vector3 rendererBoundsCenter,rendererBoundsSize,colliderBoundsCenter,colliderBoundsSize;
            public float rendererTop,colliderTop;
            public string colliderName;
        }

        [Serializable] sealed class PalaceAccessStation
        {
            public int index;
            public string segment;
            public Vector3 position;
            public float terrainHeight,probeOriginHeight,supportHeight,supportSlope,deltaFromPrevious,absoluteDelta;
            public bool supported,continuousWalkableSlope,absoluteDeltaWithinStep,deltaPassesTraversal;
            public bool strictCapsuleClear,steppedCapsuleClear,capsuleClear,headroomClear,passed;
            public string supportCollider,headroomCollider;
            public string[] supportColliders,capsuleOverlapColliders,allowableStepContactNames,raisedCapsuleOverlapColliders;
            public float capsuleRadius,capsuleBottom,capsuleTop,raisedCapsuleBottom,raisedCapsuleTop,headroom;
        }

        [Serializable] sealed class PalaceAccessReport
        {
            public string utc,scope,characterController,output;
            public float spacing,stepOffset,radius,height,skinWidth,slopeLimit,maximumAbsoluteDelta,minimumHeadroom;
            public int expectedDecks,foundDecks,boxColliderDecks,stations,supportFailures,stepViolations;
            public int rawCapsuleContactStations,rawCapsuleContacts,allowableStepContactStations,capsuleBlockedStations,headroomFailures;
            public bool passed;
            public PalaceDeckAudit[] decks;
            public PalaceAccessStation[] samples;
        }

        public static string FixPalaceAccess()
        {
            var session = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if (session == null || session.Walker == null || session.Walker.Body == null)
                throw new InvalidOperationException("World macro playtest session CharacterController is missing.");
            var corridor = GameObject.Find(RootName);
            var capital = corridor == null?null:corridor.transform.Find(CapitalGroup);
            var forecourt = capital == null?null:capital.Find("Palace_Forecourt_AssetSurface");
            if (forecourt == null) throw new InvalidOperationException("Installed palace forecourt is missing.");

            Transform palace = RequirePreservedPalace();
            var terrace = palace.Find("Stone_Terrace").GetComponent<BoxCollider>();
            CharacterController cc = session.Walker.Body;
            var decks = Enumerable.Range(0,12).Select(i => forecourt.Find("CAP_PALACE_APPROACH_DECK_"+i)).ToArray();
            if (decks.Any(d => d == null)) throw new InvalidOperationException("All 12 owned palace approach decks are required.");
            var boxes = decks.Select(d => d.GetComponent<BoxCollider>()).ToArray();
            if (boxes.Any(b => b == null || !b.enabled))
                throw new InvalidOperationException("All owned palace approach decks require their enabled BoxCollider.");
            for (int i=0; i<decks.Length; i++)
            {
                var renderers = decks[i].GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) throw new InvalidOperationException(decks[i].name+" has no owned visible geometry.");
                Bounds visible = renderers[0].bounds;
                foreach (var renderer in renderers.Skip(1)) visible.Encapsulate(renderer.bounds);
                if (Mathf.Abs(visible.max.y-boxes[i].bounds.max.y)>.02f)
                    throw new InvalidOperationException(decks[i].name+" visible top and collider top do not match.");
            }

            const float entryEdgeZ = -710.25f;
            float deckDepth = boxes.Min(b => b.bounds.size.z);
            float terraceJoinZ = terrace.bounds.max.z-.25f;
            float firstCentreZ = entryEdgeZ-deckDepth*.5f;
            float lastCentreZ = terraceJoinZ+deckDepth*.5f;
            float firstTop = TerrainY(new Vector3(-300f,0f,entryEdgeZ))+Mathf.Min(.18f,cc.stepOffset*.6f);
            float lastTop = terrace.bounds.max.y-.02f;
            float topIncrement = (lastTop-firstTop)/(decks.Length-1);
            if (firstTop>=lastTop || topIncrement>cc.stepOffset-.02f)
                throw new InvalidOperationException("The 12 source decks cannot bridge the measured palace rise within CharacterController.stepOffset.");

            for (int i=0; i<decks.Length; i++)
            {
                float t=i/(float)(decks.Length-1);
                float desiredTop=Mathf.Lerp(firstTop,lastTop,t);
                Vector3 position=decks[i].position;
                position.z=Mathf.Lerp(firstCentreZ,lastCentreZ,t);
                position.y+=desiredTop-boxes[i].bounds.max.y;
                decks[i].position=position;
                RecalculateLodBounds(decks[i]);
                RecordPlacement(decks[i].name,decks[i].name,forecourt.name,position,new Vector3(5.25f,0f,0f),0f,true);
                EditorUtility.SetDirty(decks[i]);
            }
            Physics.SyncTransforms();
            if (capital.Find(CapitalPalaceAccessComplete) == null)
            {
                var marker = new GameObject(CapitalPalaceAccessComplete);
                marker.transform.SetParent(capital,false);
            }
            EditorUtility.SetDirty(Sheet);
            AssetDatabase.SaveAssets();
            return "Palace access uses 12 visible owned deck steps: entry rise="+(firstTop-TerrainY(new Vector3(-300f,0f,entryEdgeZ))).ToString("F3")+
                "m, tread rise="+topIncrement.ToString("F3")+"m, terrace seam=0.020m; preserved terrace unchanged.";
        }

        public static string AuditPalaceAccess()
        {
            var session = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if (session == null || session.Walker == null || session.Walker.Body == null)
                throw new InvalidOperationException("World macro playtest session CharacterController is missing.");
            var corridor = GameObject.Find(RootName);
            var capital = corridor == null?null:corridor.transform.Find(CapitalGroup);
            var forecourt = capital == null?null:capital.Find("Palace_Forecourt_AssetSurface");
            if (forecourt == null) throw new InvalidOperationException("Installed palace forecourt is missing.");

            Physics.SyncTransforms();
            CharacterController cc = session.Walker.Body;
            Vector3 lossy = cc.transform.lossyScale;
            float radius = cc.radius*Mathf.Max(Mathf.Abs(lossy.x),Mathf.Abs(lossy.z));
            float height = Mathf.Max(cc.height*Mathf.Abs(lossy.y),radius*2f);
            float skin = Mathf.Max(.01f,cc.skinWidth);
            var decks = new List<PalaceDeckAudit>();
            for (int i=0; i<12; i++)
            {
                string id = "CAP_PALACE_APPROACH_DECK_"+i;
                Transform deck = forecourt.Find(id);
                var entry = new PalaceDeckAudit{id=id,found=deck!=null};
                if (deck != null)
                {
                    var renderers = deck.GetComponentsInChildren<Renderer>(true);
                    if (renderers.Length > 0)
                    {
                        Bounds bounds = renderers[0].bounds;
                        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                        entry.hasRenderer=true;entry.rendererBoundsCenter=bounds.center;entry.rendererBoundsSize=bounds.size;entry.rendererTop=bounds.max.y;
                    }
                    var box = deck.GetComponent<BoxCollider>();
                    if (box != null)
                    {
                        entry.hasBoxCollider=true;entry.boxColliderEnabled=box.enabled;entry.colliderBoundsCenter=box.bounds.center;
                        entry.colliderBoundsSize=box.bounds.size;entry.colliderTop=box.bounds.max.y;entry.colliderName=CapitalColliderName(box);
                    }
                }
                decks.Add(entry);
            }

            var positions = new List<Vector3>();
            var segments = new List<string>();
            AddPalaceAuditSegment(positions,segments,-708f,-746f,"approach",true);
            AddPalaceAuditSegment(positions,segments,-746f,-762f,"palace_gate",false);
            var samples = new List<PalaceAccessStation>();
            bool hasPrevious=false;float previousHeight=0f,previousSlope=0f;
            Collider previousSupport=null;
            for (int i=0; i<positions.Count; i++)
            {
                Vector3 sample = positions[i];
                float terrain = TerrainY(sample);
                float reference = hasPrevious?Mathf.Max(previousHeight,terrain):terrain;
                float probeY = reference+height+cc.stepOffset+.5f;
                var hits = Physics.RaycastAll(new Vector3(sample.x,probeY,sample.z),Vector3.down,20f,~0,QueryTriggerInteraction.Ignore)
                    .Where(h => !h.collider.transform.IsChildOf(cc.transform) &&
                        Vector3.Angle(h.normal,Vector3.up) <= cc.slopeLimit+.01f)
                    .OrderByDescending(h => h.point.y).ToArray();
                var station = new PalaceAccessStation
                {
                    index=i,segment=segments[i],position=sample,terrainHeight=terrain,probeOriginHeight=probeY,
                    supported=hits.Length>0,supportHeight=hits.Length>0?hits[0].point.y:-1f,
                    supportSlope=hits.Length>0?Vector3.Angle(hits[0].normal,Vector3.up):-1f,
                    supportCollider=hits.Length>0?CapitalColliderName(hits[0].collider):"",
                    supportColliders=hits.Length>0?hits.Where(h => Mathf.Abs(h.point.y-hits[0].point.y)<=.08f)
                        .Select(h => CapitalColliderName(h.collider)).Distinct().ToArray():Array.Empty<string>(),
                    capsuleRadius=Mathf.Max(.05f,radius-skin),headroom=-1f,headroomCollider=""
                };
                if (!station.supported)
                {
                    station.absoluteDeltaWithinStep=false;station.deltaPassesTraversal=false;
                    station.strictCapsuleClear=false;station.steppedCapsuleClear=false;
                    station.capsuleClear=false;station.headroomClear=false;station.passed=false;
                    station.capsuleOverlapColliders=Array.Empty<string>();
                    station.allowableStepContactNames=Array.Empty<string>();
                    station.raisedCapsuleOverlapColliders=Array.Empty<string>();samples.Add(station);continue;
                }

                station.position.y=station.supportHeight;
                station.deltaFromPrevious=hasPrevious?station.supportHeight-previousHeight:0f;
                station.absoluteDelta=Mathf.Abs(station.deltaFromPrevious);
                float slopeDeltaLimit=Mathf.Tan(cc.slopeLimit*Mathf.Deg2Rad)*.5f+.02f;
                station.continuousWalkableSlope=hasPrevious && station.absoluteDelta<=slopeDeltaLimit &&
                    (station.supportSlope>.1f || previousSlope>.1f) &&
                    (hits[0].collider==previousSupport || Mathf.Abs(station.supportSlope-previousSlope)<=2f);
                station.absoluteDeltaWithinStep=!hasPrevious || station.absoluteDelta<=cc.stepOffset+.01f;
                station.deltaPassesTraversal=station.absoluteDeltaWithinStep || station.continuousWalkableSlope;
                previousHeight=station.supportHeight;previousSlope=station.supportSlope;previousSupport=hits[0].collider;hasPrevious=true;

                float capsuleRadius = station.capsuleRadius;
                Vector3 capsuleBottom = station.position+Vector3.up*(capsuleRadius+skin);
                Vector3 capsuleTop = station.position+Vector3.up*(height-capsuleRadius+skin);
                station.capsuleBottom=capsuleBottom.y;station.capsuleTop=capsuleTop.y;
                var supportSet = new HashSet<Collider>(hits.Where(h => Mathf.Abs(h.point.y-station.supportHeight)<=.08f).Select(h => h.collider));
                var overlaps = Physics.OverlapCapsule(capsuleBottom,capsuleTop,capsuleRadius,~0,QueryTriggerInteraction.Ignore)
                    .Where(c => !c.transform.IsChildOf(cc.transform) && !supportSet.Contains(c))
                    .Distinct().ToArray();
                station.capsuleOverlapColliders=overlaps.Select(CapitalColliderName).OrderBy(n => n).ToArray();
                station.strictCapsuleClear=overlaps.Length==0;
                var allowable = overlaps.Where(c => c is BoxCollider && c.transform.parent==forecourt &&
                    c.name.StartsWith("CAP_PALACE_APPROACH_DECK_",StringComparison.Ordinal) &&
                    c.bounds.max.y>station.supportHeight+.005f && c.bounds.max.y<=station.supportHeight+cc.stepOffset+.01f).ToArray();
                station.allowableStepContactNames=allowable.Select(CapitalColliderName).OrderBy(n => n).ToArray();
                bool onlyStepContacts=overlaps.Length>0 && allowable.Length==overlaps.Length;
                station.raisedCapsuleOverlapColliders=Array.Empty<string>();
                station.steppedCapsuleClear=false;
                if (station.strictCapsuleClear) station.steppedCapsuleClear=true;
                else if (onlyStepContacts)
                {
                    float raisedSupport=allowable.Max(c => c.bounds.max.y);
                    Vector3 raisedBottom=new Vector3(sample.x,raisedSupport+capsuleRadius+skin,sample.z);
                    Vector3 raisedTop=new Vector3(sample.x,raisedSupport+height-capsuleRadius+skin,sample.z);
                    station.raisedCapsuleBottom=raisedBottom.y;station.raisedCapsuleTop=raisedTop.y;
                    var allowedSet=new HashSet<Collider>(allowable);
                    var raisedOverlaps=Physics.OverlapCapsule(raisedBottom,raisedTop,capsuleRadius,~0,QueryTriggerInteraction.Ignore)
                        .Where(c => !c.transform.IsChildOf(cc.transform) && !supportSet.Contains(c) && !allowedSet.Contains(c))
                        .Select(CapitalColliderName).Distinct().OrderBy(n => n).ToArray();
                    station.raisedCapsuleOverlapColliders=raisedOverlaps;
                    station.steppedCapsuleClear=raisedOverlaps.Length==0;
                }
                station.capsuleClear=station.strictCapsuleClear || onlyStepContacts && station.steppedCapsuleClear;

                var ceilings = Physics.RaycastAll(station.position+Vector3.up*(skin+.02f),Vector3.up,10f,~0,QueryTriggerInteraction.Ignore)
                    .Where(h => !h.collider.transform.IsChildOf(cc.transform) && !supportSet.Contains(h.collider))
                    .OrderBy(h => h.distance).ToArray();
                if (ceilings.Length > 0)
                {
                    station.headroom=ceilings[0].point.y-station.supportHeight;
                    station.headroomCollider=CapitalColliderName(ceilings[0].collider);
                }
                station.headroomClear=station.headroom<0f || station.headroom+skin>=height;
                station.passed=station.deltaPassesTraversal && station.capsuleClear && station.headroomClear;
                samples.Add(station);
            }

            var report = new PalaceAccessReport
            {
                utc=DateTime.UtcNow.ToString("o"),
                scope="Read-only edit-mode palace centreline and static step-compatibility audit; no automatic traversal claim and no scene alteration.",
                characterController=CapitalColliderName(cc),output=Output+"/palace_access.json",spacing=.5f,
                stepOffset=cc.stepOffset,radius=radius,height=height,skinWidth=skin,slopeLimit=cc.slopeLimit,
                expectedDecks=12,foundDecks=decks.Count(d => d.found),boxColliderDecks=decks.Count(d => d.hasBoxCollider && d.boxColliderEnabled),
                stations=samples.Count,supportFailures=samples.Count(s => !s.supported),
                stepViolations=samples.Count(s => s.supported && !s.deltaPassesTraversal),
                rawCapsuleContactStations=samples.Count(s => s.supported && !s.strictCapsuleClear),
                rawCapsuleContacts=samples.Where(s => s.supported).Sum(s => s.capsuleOverlapColliders.Length),
                allowableStepContactStations=samples.Count(s => s.supported && !s.strictCapsuleClear && s.capsuleClear),
                capsuleBlockedStations=samples.Count(s => s.supported && !s.capsuleClear),
                headroomFailures=samples.Count(s => s.supported && !s.headroomClear),
                maximumAbsoluteDelta=samples.Where(s => s.supported).Select(s => s.absoluteDelta).DefaultIfEmpty(0f).Max(),
                minimumHeadroom=samples.Where(s => s.headroom>=0f).Select(s => s.headroom).DefaultIfEmpty(-1f).Min(),
                decks=decks.ToArray(),samples=samples.ToArray()
            };
            report.passed=report.foundDecks==report.expectedDecks && report.boxColliderDecks==report.expectedDecks &&
                report.supportFailures==0 && report.stepViolations==0 && report.capsuleBlockedStations==0 && report.headroomFailures==0;
            EnsureFolders();
            File.WriteAllText(report.output,JsonUtility.ToJson(report,true));
            return (report.passed?"PASS":"REVIEW")+": palace access stations="+report.stations+
                ", support="+report.supportFailures+", step="+report.stepViolations+", rawCapsuleContacts="+report.rawCapsuleContacts+
                ", allowableStepContacts="+report.allowableStepContactStations+", capsule="+report.capsuleBlockedStations+
                ", headroom="+report.headroomFailures+"; "+report.output;
        }

        static void AddPalaceAuditSegment(List<Vector3> positions, List<string> segments, float startZ, float endZ,
            string segment, bool includeStart)
        {
            int steps = Mathf.CeilToInt(Mathf.Abs(endZ-startZ)/.5f);
            for (int i=includeStart?0:1; i<=steps; i++)
            {
                float t=i/(float)Mathf.Max(1,steps);
                positions.Add(new Vector3(-300f,0f,Mathf.Lerp(startZ,endZ,t)));
                segments.Add(segment);
            }
        }

        static string CapitalColliderName(Collider collider)
        {
            if (collider == null) return "";
            var names = new List<string>();
            for (Transform cursor=collider.transform; cursor!=null; cursor=cursor.parent) names.Add(cursor.name);
            names.Reverse();
            return string.Join("/",names)+" ["+collider.GetType().Name+"]";
        }

        static void PreflightCapitalSources()
        {
            foreach (string path in CapitalSources.Distinct())
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                    throw new InvalidOperationException("Reviewed capital source is missing: " + path);
        }

        static Transform RequireNamedAnchor(string name, Vector3 expected, float tolerance)
        {
            var match = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(t => t.name == name)
                .OrderBy(t => Vector3.SqrMagnitude(t.position - expected))
                .FirstOrDefault();
            if (match == null || Vector3.Distance(match.position, expected) > tolerance)
                throw new InvalidOperationException(name + " anchor is missing or moved from " + expected + ".");
            return match;
        }

        static Transform RequirePreservedPalace()
        {
            var palace = RequireNamedAnchor("Palace", PalaceAnchor, 1f);
            if (Mathf.Abs(Mathf.DeltaAngle(palace.eulerAngles.y, 180f)) > 1f)
                throw new InvalidOperationException("Palace yaw must remain 180 degrees.");
            var halls = palace.Cast<Transform>().Where(t => t.name == "Bongsudang" || t.name == "Byeolchu").ToArray();
            if (halls.Count(t => t.name == "Bongsudang") != 1 || halls.Count(t => t.name == "Byeolchu") != 2 ||
                halls.Any(t => t.GetComponent<LODGroup>() == null || t.GetComponent<LODGroup>().lodCount != 3))
                throw new InvalidOperationException("Preserved Palace Bongsudang/Byeolchu LOD assemblies are incomplete.");
            var terrace = palace.Find("Stone_Terrace");
            if (terrace == null || terrace.GetComponent<BoxCollider>() == null ||
                Mathf.Abs(terrace.localScale.x-76f) > .1f || Mathf.Abs(terrace.localScale.z-88f) > .1f)
                throw new InvalidOperationException("Palace 76x88 metre courtyard support must remain intact.");
            return palace;
        }

        static void BuildSouthGate(Transform root)
        {
            var gateRoot = ChildGroup(root, "SouthGate_AssetAssembly");
            Vector3 gateBottom = CapitalGrounded(SouthGateAnchor.x, SouthGateAnchor.z);
            var arch = PlaceCapital(gateRoot, "CAP_GATE_ARCH", CapitalArch, gateBottom, new Vector3(0f,10.5f,0f), 0f, false, .002f);
            AddLevelZeroMeshColliders(arch);
            var archRecord = Sheet.Placements.First(p => p.Id == "CAP_GATE_ARCH");
            RecordPlacement("CAP_GATE_ARCH", "CAP_GATE_ARCH", gateRoot.name,
                archRecord.Position, archRecord.Size, archRecord.Yaw, true);

            float upperFloor = gateBottom.y + 10.2f;
            int column = 0;
            foreach (float x in new[] {-5.2f, 0f, 5.2f})
            foreach (float z in new[] {-2.15f, 2.15f})
                PlaceCapital(gateRoot, "CAP_GATE_COLUMN_" + column++, CapitalGateColumn,
                    new Vector3(gateBottom.x+x, upperFloor, gateBottom.z+z), new Vector3(0f,3.8f,0f), 0f, false, .003f);
            PlaceCapital(gateRoot, "CAP_GATE_CROSSBEAM", CapitalGateCrossbeam,
                new Vector3(gateBottom.x, upperFloor+3.35f, gateBottom.z), new Vector3(15.8f,0f,0f), 0f, false, .003f);
            PlaceCapital(gateRoot, "CAP_GATE_RAFTER", CapitalGateRafter,
                new Vector3(gateBottom.x, upperFloor+4.05f, gateBottom.z), new Vector3(17.6f,0f,0f), 0f, false, .003f);
            PlaceCapital(gateRoot, "CAP_GATE_ROOF", CapitalGateRoof,
                new Vector3(gateBottom.x, upperFloor+5.1f, gateBottom.z), new Vector3(18.5f,0f,0f), 0f, false, .002f);
        }

        static void BuildSouthWall(Transform root)
        {
            var wallRoot = ChildGroup(root, "SouthWall_TerrainModules");
            for (int side=-1; side<=1; side+=2)
            for (int i=0; i<12; i++)
            {
                float x = SouthGateAnchor.x + side*(10f+i*8f);
                Vector3 p = CapitalGrounded(x, SouthGateAnchor.z);
                PlaceCapital(wallRoot, "CAP_SOUTH_WALL_" + (side<0?"W":"E") + "_" + i,
                    CapitalWall, p, new Vector3(8.15f,6.1f,2.5f), 0f, true, .0015f);
            }

            foreach (float x in new[] {SouthGateAnchor.x-72f, SouthGateAnchor.x+72f})
            {
                Vector3 p = CapitalGrounded(x, SouthGateAnchor.z);
                PlaceCapital(wallRoot, x<SouthGateAnchor.x?"CAP_WALL_PAVILION_W":"CAP_WALL_PAVILION_E",
                    CapitalPavilion, new Vector3(p.x,p.y+5.75f,p.z), new Vector3(0f,5.4f,0f), 0f, false, .0025f);
            }
        }

        static void WidenGateAssembly(Transform root, float archUniformScale, float upperFloorOffset, float firstWallOffset,
            float portalX=-279f, float portalZ=-1261f)
        {
            var gate = root.Find("SouthGate_AssetAssembly");
            var wall = root.Find("SouthWall_TerrainModules");
            if (gate == null || wall == null) throw new InvalidOperationException("Capital gate or south wall assembly is missing.");

            Vector3 portal = CapitalGrounded(portalX,portalZ);
            var arch = RequiredChild(gate,"CAP_GATE_ARCH");
            arch.position = portal;
            arch.localScale = Vector3.one*archUniformScale;
            RecalculateLodBounds(arch);
            RecordPlacement("CAP_GATE_ARCH","CAP_GATE_ARCH",gate.name,portal,
                new Vector3(0f,10.5f*archUniformScale,0f),0f,true);

            float upperFloor = portal.y+upperFloorOffset;
            var columnPositions = new[]
            {
                new Vector3(-10.8f,0f,-2.7f), new Vector3(-10.8f,0f,0f), new Vector3(-10.8f,0f,2.7f),
                new Vector3(10.8f,0f,-2.7f), new Vector3(10.8f,0f,0f), new Vector3(10.8f,0f,2.7f)
            };
            for (int i=0; i<columnPositions.Length; i++)
            {
                var placed = RequiredChild(gate,"CAP_GATE_COLUMN_"+i);
                placed.position = portal+columnPositions[i]+Vector3.up*upperFloorOffset;
                placed.localScale = Vector3.one*(5f/3.8f);
                RecalculateLodBounds(placed);
                RecordPlacement(placed.name,placed.name,gate.name,placed.position,new Vector3(0f,5f,0f),0f,false);
            }
            RefitGatePart(gate,"CAP_GATE_CROSSBEAM",portal+Vector3.up*(upperFloor-portal.y+4.4f),31f/15.8f,new Vector3(31f,0f,0f));
            RefitGatePart(gate,"CAP_GATE_RAFTER",portal+Vector3.up*(upperFloor-portal.y+5.2f),34f/17.6f,new Vector3(34f,0f,0f));
            RefitGatePart(gate,"CAP_GATE_ROOF",portal+Vector3.up*(upperFloor-portal.y+6.2f),35f/18.5f,new Vector3(35f,0f,0f));

            for (int side=-1; side<=1; side+=2)
            for (int i=0; i<12; i++)
            {
                string id = "CAP_SOUTH_WALL_"+(side<0?"W":"E")+"_"+i;
                var placed = RequiredChild(wall,id);
                Vector3 p = CapitalGrounded(portal.x+side*(firstWallOffset+i*8f),portal.z);
                placed.position = p;
                RecordPlacement(id,id,wall.name,p,new Vector3(8.15f,6.1f,2.5f),0f,true);
            }
            foreach (int side in new[] {-1,1})
            {
                string id = side<0?"CAP_WALL_PAVILION_W":"CAP_WALL_PAVILION_E";
                var placed = RequiredChild(wall,id);
                Vector3 ground = CapitalGrounded(portal.x+side*72f,portal.z);
                placed.position = ground+Vector3.up*5.75f;
                RecordPlacement(id,id,wall.name,placed.position,new Vector3(0f,5.4f,0f),0f,false);
            }
        }

        static void NormalizeGateAssembly(Transform root, float portalX, float portalZ)
        {
            var gate = root.Find("SouthGate_AssetAssembly");
            var wall = root.Find("SouthWall_TerrainModules");
            if (gate == null || wall == null) throw new InvalidOperationException("Capital gate or south wall assembly is missing.");

            const float scale = 1.25f;
            Vector3 portal = CapitalGrounded(portalX,portalZ);
            var arch = RequiredChild(gate,"CAP_GATE_ARCH");
            arch.position = portal;
            arch.localScale = Vector3.one*scale;
            RecalculateLodBounds(arch);
            RecordPlacement("CAP_GATE_ARCH","CAP_GATE_ARCH",gate.name,portal,new Vector3(0f,10.5f*scale,0f),0f,true);

            float upperFloor = portal.y+12.75f;
            var columnPositions = new[]
            {
                new Vector3(-5.2f,0f,-2.15f), new Vector3(-5.2f,0f,2.15f),
                new Vector3(0f,0f,-2.15f), new Vector3(0f,0f,2.15f),
                new Vector3(5.2f,0f,-2.15f), new Vector3(5.2f,0f,2.15f)
            };
            for (int i=0; i<columnPositions.Length; i++)
            {
                var placed = RequiredChild(gate,"CAP_GATE_COLUMN_"+i);
                Vector3 offset = columnPositions[i]*scale;
                placed.position = new Vector3(portal.x+offset.x,upperFloor,portal.z+offset.z);
                placed.localScale = Vector3.one*scale;
                RecalculateLodBounds(placed);
                RecordPlacement(placed.name,placed.name,gate.name,placed.position,new Vector3(0f,3.8f*scale,0f),0f,false);
            }
            RefitGatePart(gate,"CAP_GATE_CROSSBEAM",portal+Vector3.up*(12.75f+3.35f*scale),scale,new Vector3(15.8f*scale,0f,0f));
            RefitGatePart(gate,"CAP_GATE_RAFTER",portal+Vector3.up*(12.75f+4.05f*scale),scale,new Vector3(17.6f*scale,0f,0f));
            RefitGatePart(gate,"CAP_GATE_ROOF",portal+Vector3.up*(12.75f+5.1f*scale),scale,new Vector3(18.5f*scale,0f,0f));

            const float firstWallOffset = 12.5f;
            for (int side=-1; side<=1; side+=2)
            for (int i=0; i<12; i++)
            {
                string id = "CAP_SOUTH_WALL_"+(side<0?"W":"E")+"_"+i;
                var placed = RequiredChild(wall,id);
                Vector3 p = CapitalGrounded(portal.x+side*(firstWallOffset+i*8f),portal.z);
                placed.position = p;
                RecordPlacement(id,id,wall.name,p,new Vector3(8.15f,6.1f,2.5f),0f,true);
            }
            foreach (int side in new[] {-1,1})
            {
                string id = side<0?"CAP_WALL_PAVILION_W":"CAP_WALL_PAVILION_E";
                var placed = RequiredChild(wall,id);
                Vector3 ground = CapitalGrounded(portal.x+side*72f,portal.z);
                placed.position = ground+Vector3.up*5.75f;
                RecordPlacement(id,id,wall.name,placed.position,new Vector3(0f,5.4f,0f),0f,false);
            }
        }

        static void AddGateCloseView()
        {
            var views = (Sheet.Views ?? Array.Empty<Oheangbu.Data.World.WorldMacroVisualCorridorSO.View>())
                .Where(v => v != null && v.Id != "gate_close")
                .Concat(new[]
                {
                    new Oheangbu.Data.World.WorldMacroVisualCorridorSO.View
                    {
                        Id="gate_close", Eye=CapitalEye(-279f,-1238f), Target=CapitalGrounded(-279f,-1276f)+Vector3.up*8f
                    }
                }).ToArray();
            SetViews(views);
        }

        static void RefitGatePart(Transform gate, string id, Vector3 position, float uniformScale, Vector3 envelope)
        {
            var placed = RequiredChild(gate,id);
            placed.position = position;
            placed.localScale = Vector3.one*uniformScale;
            RecalculateLodBounds(placed);
            RecordPlacement(id,id,gate.name,position,envelope,0f,false);
        }

        static void RecalculateLodBounds(Transform placed)
        {
            var group = placed.GetComponent<LODGroup>();
            if (group != null) group.RecalculateBounds();
        }

        static Transform RequiredChild(Transform parent, string id)
        {
            var child = parent.Find(id);
            if (child == null) throw new InvalidOperationException("Required capital placement is missing: " + id);
            return child;
        }

        static int ReplaceCapitalProxyBuildings(Transform capitalRoot)
        {
            var city = FindCapitalMassingRoot();
            var proxies = city.Cast<Transform>()
                .Where(t => t.name == "Massing_Walls" && t.GetComponent<Renderer>() != null)
                .Select(t => t.GetComponent<Renderer>())
                .OrderBy(r => r.bounds.center.z).ThenBy(r => r.bounds.center.x).ToArray();
            if (proxies.Length == 0) throw new InvalidOperationException("Hwanggyeong proxy house footprints are missing.");

            var replacements = ChildGroup(capitalRoot,"Citywide_OwnedBuildingReplacement");
            Material foundationMaterial = CapitalFoundationMaterial();
            for (int i=0; i<proxies.Length; i++)
            {
                Bounds footprint = proxies[i].bounds;
                float horizontalSpan = Mathf.Max(footprint.size.x,footprint.size.z);
                bool fullHouse = horizontalSpan >= 21f || i%4 == 0;
                string source = fullHouse?CapitalHouse:CapitalPavilion;
                float height = Mathf.Clamp(footprint.size.y,4f,6f);
                float yaw = fullHouse && footprint.size.z > footprint.size.x?90f:0f;
                Vector3 bottom = new Vector3(footprint.center.x,footprint.min.y,footprint.center.z);
                PlaceCapital(replacements,"CAP_CITY_OWNED_"+i.ToString("D2"),source,bottom,
                    new Vector3(0f,height,0f),yaw,true,.0025f);
            }

            foreach (Transform child in city)
            {
                if (child.name != "Massing_Walls" && child.name != "Massing_Roof" && child.name != "Massing_Foundation") continue;
                if (child.name == "Massing_Foundation")
                {
                    ApplyCapitalFoundationSurface(child,foundationMaterial);
                    continue;
                }
                foreach (var renderer in child.GetComponents<Renderer>())
                {
                    renderer.enabled = false;
                    EditorUtility.SetDirty(renderer);
                }
                foreach (var collider in child.GetComponents<Collider>())
                {
                    collider.enabled = false;
                    EditorUtility.SetDirty(collider);
                }
            }
            return proxies.Length;
        }

        static Transform FindCapitalMassingRoot()
        {
            var city = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                .Where(t => t.name == "Hwanggyeong" && Vector3.Distance(t.position,HwanggyeongAnchor) < 1f)
                .OrderByDescending(t => t.Cast<Transform>().Count(c => c.name == "Massing_Walls"))
                .FirstOrDefault();
            if (city == null) throw new InvalidOperationException("Hwanggyeong city massing root is missing.");
            return city;
        }

        static void RestoreCapitalFoundationSurfaces()
        {
            Material material = CapitalFoundationMaterial();
            foreach (Transform child in FindCapitalMassingRoot())
                if (child.name == "Massing_Foundation") ApplyCapitalFoundationSurface(child,material);
        }

        static Material CapitalFoundationMaterial()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(CapitalWall);
            if (source == null) throw new InvalidOperationException("Capital wall source is missing for foundation surfacing.");
            var sourceMaterial = source.GetComponentsInChildren<Renderer>(true)
                .SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m != null);
            if (sourceMaterial == null) throw new InvalidOperationException("Capital wall source has no material for foundation surfacing.");
            return Surface(sourceMaterial);
        }

        static void ApplyCapitalFoundationSurface(Transform foundation, Material material)
        {
            foreach (var renderer in foundation.GetComponents<Renderer>())
            {
                renderer.sharedMaterials = Enumerable.Repeat(material,Mathf.Max(1,renderer.sharedMaterials.Length)).ToArray();
                renderer.enabled = true;
                EditorUtility.SetDirty(renderer);
            }
        }

        static void BuildRepresentativeStreet(Transform root)
        {
            var streetRoot = ChildGroup(root, "GateStreet_HouseFrontage");
            var rows = new[]
            {
                new {Centre=new Vector2(-208f,-1184f), Yaw=-50f},
                new {Centre=new Vector2(-176f,-1120f), Yaw=-72f},
                new {Centre=new Vector2(-164f,-1068f), Yaw=-82f},
                new {Centre=new Vector2(-160f,-1008f), Yaw=-90f},
                new {Centre=new Vector2(-160f,-948f), Yaw=-90f},
                new {Centre=new Vector2(-192f,-725f), Yaw=-127f},
                new {Centre=new Vector2(-256f,-656f), Yaw=-136f}
            };
            for (int row=0; row<rows.Length; row++)
            for (int side=-1; side<=1; side+=2)
            {
                float radians = rows[row].Yaw*Mathf.Deg2Rad;
                var sideDirection = new Vector2(Mathf.Sin(radians),Mathf.Cos(radians));
                Vector2 footprint = rows[row].Centre + sideDirection*(side*18f);
                Vector3 p = CapitalGrounded(footprint.x, footprint.y);
                string id = "CAP_HOUSE_R" + row + (side<0?"_E":"_W");
                PlaceCapital(streetRoot, id, CapitalHouse, p, new Vector3(18f,4.4f,5.5f), rows[row].Yaw, true, .005f);
            }
        }

        static void BuildMarketEdges(Transform root)
        {
            var market = ChildGroup(root, "GateStreet_MarketEdges");
            PlaceGrounded(market,"CAP_MARKET_BOX_W0",CapitalWoodenBox,-176f,-1087f,new Vector3(1.1f,1.2f,1.1f),17f,true,.004f);
            PlaceGrounded(market,"CAP_MARKET_BOX_W1",CapitalWoodenBox,-178f,-1085f,new Vector3(.9f,1f,.9f),-8f,true,.004f);
            PlaceGrounded(market,"CAP_MARKET_SACK_W0",CapitalRiceSack,-173.5f,-1084f,new Vector3(1.05f,.9f,.55f),73f,false,.004f);
            PlaceGrounded(market,"CAP_MARKET_BASKET_W0",CapitalBasket,-174.5f,-1086f,new Vector3(.7f,0f,0f),-21f,false,.004f);
            PlaceGrounded(market,"CAP_MARKET_POT_W0",CapitalPot,-174.3f,-1084.5f,new Vector3(.48f,0f,0f),11f,false,.004f);

            PlaceGrounded(market,"CAP_MARKET_BOX_E0",CapitalWoodenBox,-149f,-1018f,new Vector3(1.15f,1.25f,1.15f),-14f,true,.004f);
            PlaceGrounded(market,"CAP_MARKET_SACK_E0",CapitalRiceSack,-146.7f,-1016.5f,new Vector3(1.1f,.9f,.55f),101f,false,.004f);
            PlaceGrounded(market,"CAP_MARKET_SACK_E1",CapitalRiceSack,-148.5f,-1015f,new Vector3(.9f,.75f,.5f),82f,false,.004f);
            PlaceGrounded(market,"CAP_MARKET_BASKET_E0",CapitalBasket,-144.8f,-1017.5f,new Vector3(.7f,0f,0f),32f,false,.004f);
            PlaceGrounded(market,"CAP_MARKET_POT_E0",CapitalPot,-144.2f,-1015.7f,new Vector3(.5f,0f,0f),-26f,false,.004f);

            PlaceGrounded(market,"CAP_MARKET_TIMBER_N0",CapitalWoodLog,-242f,-681f,new Vector3(.45f,.35f,2.8f),6f,true,.004f);
            PlaceGrounded(market,"CAP_MARKET_TIMBER_N1",CapitalWoodLog,-240.9f,-681.4f,new Vector3(.45f,.35f,2.5f),-4f,true,.004f);
        }

        static void BuildCityCentreSilhouette(Transform root)
        {
            var centre = ChildGroup(root, "CityCentre_RoofsAndPropertyWalls");
            PlaceGrounded(centre,"CAP_CITY_PAVILION_W",CapitalPavilion,-347f,-653f,new Vector3(0f,5.7f,0f),12f,true,.003f);
            PlaceGrounded(centre,"CAP_CITY_PAVILION_N",CapitalPavilion,-294f,-603f,new Vector3(0f,6.1f,0f),180f,true,.003f);

            foreach (var item in new[]
            {
                new {Id="CAP_PROPERTY_WALL_W0",X=-360f,Z=-680f,Yaw=0f,Path=CapitalStraightWallLong},
                new {Id="CAP_PROPERTY_WALL_W1",X=-360f,Z=-674.8f,Yaw=0f,Path=CapitalStraightWallLong},
                new {Id="CAP_PROPERTY_WALL_E0",X=-205f,Z=-690f,Yaw=0f,Path=CapitalStraightWallLong},
                new {Id="CAP_PROPERTY_WALL_E1",X=-205f,Z=-684.8f,Yaw=0f,Path=CapitalStraightWallLong},
                new {Id="CAP_PROPERTY_WALL_NW",X=-337f,Z=-625f,Yaw=90f,Path=CapitalStraightWallShort},
                new {Id="CAP_PROPERTY_WALL_NE",X=-259f,Z=-625f,Yaw=90f,Path=CapitalStraightWallShort}
            })
                PlaceGrounded(centre,item.Id,item.Path,item.X,item.Z,new Vector3(0f,1.8f,item.Path==CapitalStraightWallLong?4.3f:2.2f),item.Yaw,true,.002f);
        }

        static void BuildPalaceForecourt(Transform root)
        {
            var forecourt = ChildGroup(root, "Palace_Forecourt_AssetSurface");

            // Reviewed brick floor lies directly over the retained courtyard support collider.
            for (int i=0; i<20; i++)
            {
                float z = -751.5f-i*2.82f;
                PlaceCapital(forecourt,"CAP_PALACE_PAVING_"+i,CapitalBrickPaving,
                    new Vector3(PalaceAnchor.x,PalaceAnchor.y+.075f,z),new Vector3(2.9f,0f,0f),90f,false,.002f);
            }

            // A thin paved spur connects the reserved capital road near (-200,-715) to the palace axis.
            for (int i=0; i<18; i++)
            {
                float t = i/17f, x = Mathf.Lerp(-205f,-294f,t), z = Mathf.Lerp(-713f,-713.5f,t);
                PlaceCapital(forecourt,"CAP_PALACE_SPUR_"+i,CapitalBrickPaving,
                    CapitalGrounded(x,z)+Vector3.up*.035f,new Vector3(2.9f,0f,0f),90f,false,.002f);
            }

            // Six broad modules make a shallow, terrain-aware approach instead of the former abrupt stair stack.
            const int deckCount = 12;
            float startZ = -713.5f, endZ = -741f;
            float startBottom = TerrainY(new Vector3(PalaceAnchor.x,0f,startZ))+.03f;
            float thresholdBottom = PalaceAnchor.y-.51f;
            for (int i=0; i<deckCount; i++)
            {
                float t = i/(float)(deckCount-1), z = Mathf.Lerp(startZ,endZ,t);
                float terrain = TerrainY(new Vector3(PalaceAnchor.x,0f,z))+.03f;
                float y = Mathf.Max(terrain,Mathf.Lerp(startBottom,thresholdBottom,t));
                PlaceCapital(forecourt,"CAP_PALACE_APPROACH_DECK_"+i,CapitalFloor,
                    new Vector3(PalaceAnchor.x,y,z),new Vector3(5.25f,0f,0f),0f,true,.002f);
            }

            for (int side=-1; side<=1; side+=2)
            for (int i=0; i<4; i++)
            {
                float z = -716f-i*7.6f;
                float y = Mathf.Max(TerrainY(new Vector3(PalaceAnchor.x,0f,z))+.02f,
                    Mathf.Lerp(startBottom,thresholdBottom,i/3f));
                PlaceCapital(forecourt,"CAP_PALACE_EDGE_"+(side<0?"W":"E")+"_"+i,CapitalStoneCourse,
                    new Vector3(PalaceAnchor.x+side*3.2f,y,z),new Vector3(7.7f,0f,0f),90f,false,.002f);
            }
        }

        static Transform ChildGroup(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child != null) return child;
            child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        static Vector3 CapitalGrounded(float x, float z)
        {
            var p = new Vector3(x,0f,z);
            p.y = TerrainY(p);
            return p;
        }

        static Vector3 CapitalEye(float x, float z)
        {
            Vector3 p = CapitalGrounded(x,z);
            p.y += 1.55f;
            return p;
        }

        static Transform PlaceGrounded(Transform parent, string id, string source, float x, float z,
            Vector3 size, float yaw, bool solid, float cull)
        {
            return PlaceCapital(parent,id,source,CapitalGrounded(x,z),size,yaw,solid,cull);
        }

        /// <summary>Placement rows are the frozen authoring poses; existing rows may be adjusted in the SO before a clean reinstall.</summary>
        static Transform PlaceCapital(Transform parent, string id, string source, Vector3 defaultPosition,
            Vector3 defaultSize, float defaultYaw, bool solid, float finalCull)
        {
            var configured = Sheet.Placements.FirstOrDefault(p => p.Id == id);
            Vector3 position = configured == null ? defaultPosition : configured.Position;
            Vector3 size = configured == null ? defaultSize : configured.Size;
            float yaw = configured == null ? defaultYaw : configured.Yaw;
            var placed = PlaceSource(parent,id,source,position,size,yaw,solid);
            TuneFinalCull(placed,finalCull);
            return placed;
        }

        static void TuneFinalCull(Transform placed, float finalCull)
        {
            var group = placed.GetComponent<LODGroup>();
            if (group == null) return;
            var lods = group.GetLODs();
            if (lods.Length == 0) return;
            lods[lods.Length-1].screenRelativeTransitionHeight = finalCull;
            group.SetLODs(lods);
            group.RecalculateBounds();
        }

        static void AddLevelZeroMeshColliders(Transform placed)
        {
            foreach (var filter in placed.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.sharedMesh != null && f.name.StartsWith("Surface_0_",StringComparison.Ordinal)))
            {
                var collider = filter.GetComponent<MeshCollider>();
                if (collider == null) collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
            }
        }

        static int ReplaceCapitalMassingInstances()
        {
            int count = 0;
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!renderer.enabled || !ReplaceableMassingNames.Contains(renderer.gameObject.name)) continue;
                Vector3 p = renderer.bounds.center;
                if (p.x < -430f || p.x > -120f || p.z < -1325f || p.z > -560f) continue;
                renderer.enabled = false;
                EditorUtility.SetDirty(renderer);
                // Covered foundations remain as support. Other hidden proxy colliders would become invisible blockers.
                if (renderer.gameObject.name != "Massing_Foundation")
                    foreach (var collider in renderer.GetComponents<Collider>())
                    {
                        collider.enabled = false;
                        EditorUtility.SetDirty(collider);
                    }
                count++;
            }
            return count;
        }

        static void HideCoveredPalacePrimitives(Transform palace)
        {
            foreach (Transform child in palace)
            {
                bool steps = child.name.StartsWith("Access_Step_",StringComparison.Ordinal);
                bool coveredPath = child.name == "Processional_Stone_Path";
                if (!steps && !coveredPath) continue;
                foreach (var renderer in child.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.enabled = false;
                    EditorUtility.SetDirty(renderer);
                }
                if (steps) foreach (var collider in child.GetComponentsInChildren<Collider>(true))
                {
                    collider.enabled = false;
                    EditorUtility.SetDirty(collider);
                }
            }
        }

        static void ValidateOpenGate(Transform root)
        {
            var gate = root.Find("SouthGate_AssetAssembly");
            if (gate == null || gate.Find("CAP_GATE_ARCH") == null)
                throw new InvalidOperationException("Capital gate assembly is incomplete.");
            Vector3 passage = gate.Find("CAP_GATE_ARCH").position+Vector3.up*1.1f;
            foreach (var box in gate.GetComponentsInChildren<BoxCollider>(true))
                if (box.enabled && box.bounds.Contains(passage))
                    throw new InvalidOperationException("A box collider blocks the SouthGate opening: " + box.name);
            if (gate.Find("CAP_GATE_ARCH").GetComponentsInChildren<MeshCollider>(true).Length == 0)
                throw new InvalidOperationException("SouthGate arch needs its opening-preserving mesh collider.");
        }

        static void ValidateStreetClearance(Transform root)
        {
            var route = WorldMacroBuilder.Sheet.Routes.FirstOrDefault(r => r.Id == "Road_Gate_CapitalReservation");
            if (route == null || route.Points == null || route.Points.Length < 2)
                throw new InvalidOperationException("Road_Gate_CapitalReservation is required for capital clearance validation.");
            float roadEdge = route.Width*.5f;
            foreach (Transform placed in root.GetComponentsInChildren<Transform>(true))
            {
                bool house = placed.name.StartsWith("CAP_HOUSE_",StringComparison.Ordinal);
                bool market = placed.name.StartsWith("CAP_MARKET_",StringComparison.Ordinal);
                if (!house && !market) continue;
                var renderers = placed.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) throw new InvalidOperationException("Capital placement has no renderer: " + placed.name);
                Bounds bounds = renderers[0].bounds;
                foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                float distance = float.MaxValue;
                for (int i=1; i<route.Points.Length; i++)
                    distance = Mathf.Min(distance,WorldMacroTerrain.SegmentDistance(bounds.center.x,bounds.center.z,
                        route.Points[i-1],route.Points[i],out _));
                float horizontalRadius = Mathf.Min(bounds.extents.x,bounds.extents.z);
                float required = roadEdge+(house?2f:.65f);
                if (distance-horizontalRadius < required)
                    throw new InvalidOperationException(placed.name + " intrudes into the capital road clearance (" +
                        (distance-horizontalRadius).ToString("F2") + "m < " + required.ToString("F2") + "m).");
            }
        }
    }
}
