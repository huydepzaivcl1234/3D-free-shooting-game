using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MiningSimulator.Ores
{
    /// <summary>Owns the scene-authored presentation while the gameplay clock is paused.</summary>
    public sealed class MenuDiorama : MonoBehaviour
    {
        [UnityEngine.Serialization.FormerlySerializedAs("findMiningWall")]
        [SerializeField] private bool findOpenView = true;
        [SerializeField] private Transform turntable, clouds;
        [SerializeField] private AnimationClip idle, startupBackstep;
        [SerializeField] private Light sunset, lantern;
        [SerializeField] private Camera mainShot, skinShot;
        [SerializeField] private Volume atmosphere;
        [SerializeField] private Color horizon = new Color32(143, 180, 201, 255);
        [SerializeField] private Color warmFog = new Color32(216, 184, 144, 255);
        [SerializeField, Min(0)] private float fogDensity = .015f;
        private PlayableGraph graph;
        private Vector3 layoutPosition, cloudPosition;
        private Quaternion layoutRotation;
        private float start, squashTime, lanternPower;
        private bool positioned, running;
        private VolumeProfile profile;
        private DayNightSystem clock;
        private Light worldSun, previousSun;
        private Camera view;
        public const string ScenePath = "Assets/Scenes/MainMenu.unity";
        public const string LayerName = "MenuWorld";
        [SerializeField] private Animator menuAnimator;
        public Transform Avatar => menuAnimator ? menuAnimator.transform : null;
        private Transform visualPivot => Avatar;
        private bool worldSunState, previousFog;
        private FogMode previousFogMode;
        private Color previousSky, previousGround, previousEquator, previousFogColor, previousClear;
        private float previousDensity;
        private AmbientMode previousAmbient;
        private DepthOfField depth;
        private UniversalAdditionalCameraData cameraData;
        private AntialiasingMode previousAA;
        private AntialiasingQuality previousAAQuality;
        private bool previouslyActive, previousPost;

        internal Transform Turntable => turntable;
        internal bool IsBound => turntable && sunset && mainShot && skinShot && atmosphere && atmosphere.sharedProfile;

        private void Start()
        {
            if (!running) gameObject.SetActive(false);
        }

        public void Begin(Camera camera)
        {
            view = camera;
            cameraData = camera.GetUniversalAdditionalCameraData();
            previousAA = cameraData.antialiasing;
            previousAAQuality = cameraData.antialiasingQuality;
            previousPost = cameraData.renderPostProcessing;
            ApplyPreviewCameraQuality();
            running = true;
            previouslyActive = gameObject.activeSelf;
            positioned = false;
            start = Time.unscaledTime;
            squashTime = -10f;
            layoutPosition = transform.position;
            layoutRotation = transform.rotation;
            if (clouds) cloudPosition = clouds.localPosition;
            if (lantern) lanternPower = lantern.intensity;
            previousClear = camera.backgroundColor;
            previousFog = RenderSettings.fog;
            previousFogMode = RenderSettings.fogMode;
            previousFogColor = RenderSettings.fogColor;
            previousDensity = RenderSettings.fogDensity;
            previousAmbient = RenderSettings.ambientMode;
            previousSky = RenderSettings.ambientSkyColor;
            previousEquator = RenderSettings.ambientEquatorColor;
            previousGround = RenderSettings.ambientGroundColor;
            clock = FindAnyObjectByType<DayNightSystem>();
            if (clock) clock.SetMenuLightingPaused(true);
            previousSun = RenderSettings.sun;
            worldSun = previousSun;
            if (!worldSun)
                foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (light.type == LightType.Directional && light != sunset) { worldSun = light; break; }
            worldSunState = worldSun && worldSun.enabled;
            if (worldSun) worldSun.enabled = false;
            RenderSettings.sun = sunset;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = horizon;
            RenderSettings.ambientEquatorColor = new Color32(103, 110, 104, 255);
            RenderSettings.ambientGroundColor = new Color32(58, 74, 54, 255);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = warmFog;
            RenderSettings.fogDensity = fogDensity;
            camera.backgroundColor = horizon;
            gameObject.SetActive(true);
            profile = atmosphere.profile;
            profile.TryGet(out depth);
            if (Application.isPlaying) PlayMenuClip(idle);
        }

        public void End()
        {
            if (!running) return;
            running = false;
            if (graph.IsValid()) graph.Destroy();
            if (visualPivot) visualPivot.localPosition = Vector3.zero;
            if (cameraData)
            {
                var graphics = Application.isPlaying ? FindAnyObjectByType<PlayerGraphicsOptions>() : null;
                var values = graphics ? graphics.Values : null;
                cameraData.antialiasing = values != null ? (AntialiasingMode)values.aaMode : previousAA;
                cameraData.antialiasingQuality = values != null ? (AntialiasingQuality)values.aaQuality : previousAAQuality;
                cameraData.renderPostProcessing = values != null ? values.postProcessing : previousPost;
            }
            if (view) view.backgroundColor = previousClear;
            RenderSettings.sun = previousSun;
            RenderSettings.fog = previousFog;
            RenderSettings.fogMode = previousFogMode;
            RenderSettings.fogColor = previousFogColor;
            RenderSettings.fogDensity = previousDensity;
            RenderSettings.ambientMode = previousAmbient;
            RenderSettings.ambientSkyColor = previousSky;
            RenderSettings.ambientEquatorColor = previousEquator;
            RenderSettings.ambientGroundColor = previousGround;
            if (worldSun) worldSun.enabled = worldSunState;
            if (clock) clock.SetMenuLightingPaused(false);
            gameObject.SetActive(!Application.isPlaying && previouslyActive);
            transform.SetPositionAndRotation(layoutPosition, layoutRotation);
        }



        private void PlayMenuClip(AnimationClip clip)
        {
            if (!clip || !menuAnimator) return;
            if (graph.IsValid()) graph.Destroy();
            graph = PlayableGraph.Create("CapsuleBoy menu animation");
            graph.SetTimeUpdateMode(DirectorUpdateMode.UnscaledGameTime);
            var playback = AnimationClipPlayable.Create(graph, clip);
            AnimationPlayableOutput.Create(graph, "Menu pose", menuAnimator).SetSourcePlayable(playback);
            graph.Play();
        }

        internal void BeginStartupBackstep() => PlayMenuClip(startupBackstep);

        internal Vector3 ApplyStartupBackstep(float distance)
        {
            if (!visualPivot) return turntable.position;
            var retreat = -(visualPivot.localRotation * Vector3.forward);
            retreat.y = 0f;
            visualPivot.localPosition += retreat.normalized * distance;
            return visualPivot.position;
        }

        internal void Punch() => squashTime = Time.unscaledTime;

        internal void Tick(float focusDistance)
        {
            ApplyPreviewCameraQuality();
            float now = Time.unscaledTime;
            float t = now - start;
            float breath = Mathf.Sin(t * 1.8f) * .012f;
            float phase = (now - squashTime) / .31f;
            float squash = phase < 1f ? Mathf.Sin(phase * Mathf.PI * 2f) * (1f - phase) * .12f : 0f;
            if (visualPivot)
            {
                visualPivot.localScale = .72f * new Vector3(1f + squash * .7f, 1f + breath - squash, 1f + squash * .7f);
                visualPivot.localPosition = new Vector3(Mathf.Sin(t * 1.06f) * .013f, .007f, 0f);
            }
            if (lantern) lantern.intensity = lanternPower * (.94f + .045f * Mathf.Sin(t * 6.1f) + .025f * Mathf.Sin(t * 10.9f));
            if (clouds) clouds.localPosition = cloudPosition + Vector3.right * (Mathf.Sin(t * .045f) * 1.8f);
            SetFocus(focusDistance);
        }

        public void SetFocus(float focusDistance)
        {
            if (depth)
            {
                depth.gaussianStart.Override(focusDistance + 1.6f);
                depth.gaussianEnd.Override(focusDistance + 11f);
            }
        }

        private void ApplyPreviewCameraQuality()
        {
            if (!cameraData) return;
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraData.antialiasingQuality = AntialiasingQuality.High;
        }

        public void CameraPose(bool selecting, float aspect, float distanceScale, float viewportShift,
            out Vector3 position, out Quaternion rotation, out float fov, out float distance)
        {
            var shot = selecting ? skinShot : mainShot;
            fov = shot.fieldOfView;
            var offset = transform.TransformDirection(shot.transform.localPosition);
            rotation = transform.rotation * shot.transform.localRotation;
            distance = offset.magnitude * distanceScale;
            float side = viewportShift * 2f * distance * Mathf.Tan(fov * .5f * Mathf.Deg2Rad) * aspect;
            position = turntable.position + Vector3.up * .94f - rotation * new Vector3(side, 0, distance);
        }

        public bool FrameTerrain(TerrainGenerator terrain)
        {
            if (positioned || !terrain) return false;
            var root = terrain.transform.Find("Terrain Blocks");
            if (!root || !root.TryGetComponent<MeshCollider>(out var ground)) return false;
            positioned = true;
            var preferred = transform.position;
            var best = preferred;
            var preferredFacing = transform.forward;
            var facing = preferredFacing;
            float bestScore = float.NegativeInfinity;
            var directions = new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
            if (findOpenView)
            {
                int obstacles = LayerMask.GetMask("MapObstacle");
                var portal = transform.Find("Props/Stone Portal");
                var portalOffset = portal ? portal.localPosition : Vector3.zero;
                for (int z = -15; z <= 15; z++)
                for (int x = -15; x <= 15; x++)
                {
                    var candidate = preferred + new Vector3(x * 3f, 0, z * 3f);
                    if (!Ground(candidate, out var floor) ||
                        Physics.CheckSphere(floor + Vector3.up * .85f, .95f, obstacles, QueryTriggerInteraction.Ignore)) continue;
                    foreach (var front in directions)
                    {
                        Vector3 side = Vector3.Cross(Vector3.up, front);
                        var eye = floor + front * 5.8f - side * 1.28f + Vector3.up * 1.5f;
                        if (!Ground(eye, out var cameraFloor) || Mathf.Abs(floor.y - cameraFloor.y) > .45f) continue;
                        if (portal && (!Ground(floor + front * portalOffset.z + side * portalOffset.x, out var portalFloor) ||
                            Mathf.Abs(portalFloor.y - floor.y) > .25f)) continue;
                        if (Physics.Linecast(floor + Vector3.up, eye, obstacles, QueryTriggerInteraction.Ignore) ||
                            ground.Raycast(new Ray(eye, -front), out _, 16f)) continue;

                        // Match the camera composition with clear sky above a distant terrain edge.
                        int openSky = 0, distantWall = 0;
                        for (int sample = -1; sample <= 1; sample++)
                        {
                            var heading = -front + side * (sample * .29f);
                            if (!ground.Raycast(new Ray(eye, (heading + Vector3.up * .065f).normalized), out _, 52f))
                                openSky++;
                            if (ground.Raycast(new Ray(eye, (heading - Vector3.up * .06f).normalized), out var wall, 55f) &&
                                wall.distance > 17f && wall.normal.y < .5f) distantWall++;
                        }
                        bool looksOverEdge = !Ground(floor - front * 9f, out var backdrop) ||
                            backdrop.y < floor.y - 1.5f;
                        float score = openSky * 9f + distantWall * 7f + (looksOverEdge ? 8f : 0f) -
                            (candidate - preferred).sqrMagnitude * .003f + Vector3.Dot(front, preferredFacing) * .5f;
                        if (score <= bestScore) continue;
                        bestScore = score;
                        best = floor;
                        facing = front;
                    }
                }
            }
            if (float.IsNegativeInfinity(bestScore) && Ground(preferred, out var floorAtAnchor)) best = floorAtAnchor;
            transform.SetPositionAndRotation(best, Quaternion.LookRotation(facing, Vector3.up));
            return true;

            bool Ground(Vector3 point, out Vector3 surface)
            {
                bool hit = ground.Raycast(new Ray(new Vector3(point.x, ground.bounds.max.y + 2, point.z), Vector3.down),
                    out var info, ground.bounds.size.y + 4);
                surface = info.point;
                return hit && info.normal.y > .985f;
            }
        }
    }
}
