#nullable enable
using System;
using System.Collections.Generic;
using Elts.Geometry;
using Elts.Scenario;
using UnityEngine;

namespace Elts.Operator
{
    /// <summary>Presentation tuning in virtual metres and seconds; not apparatus measurements.</summary>
    [Serializable]
    public sealed class DevelopmentMotionSettings
    {
        [Min(.1f)] public float walkSpeedMps = 1.15f;
        [Min(.1f)] public float jogSpeedMps = 2.6f;
        [Min(.1f)] public float crouchSpeedMps = .65f;
        [Min(.01f)] public float blendSeconds = .16f;
        [Min(1f)] public float turnDegreesPerSecond = 420f;
    }

    /// <summary>Sourced CC0 training mannequin. Presentation only: scenario geometry
    /// remains authoritative for hit regions, movement and recorded target positions.</summary>
    internal sealed class DevelopmentHumanoidVisual : MonoBehaviour
    {
        private const string ModelResource = "ELTS/Mannequin/UAL1_Standard";
        private GameObject model = null!;
        private Animation animationPlayer = null!;
        private AnimationState idle = null!, walk = null!, jog = null!, crouchIdle = null!, crouchWalk = null!;
        private Transform leftFoot = null!, rightFoot = null!;
        private DevelopmentMotionSettings settings = null!;
        private double floorY;
        private Vector3 fittedPosition, fittedScale;
        private float standingFootY, speed, yaw, gaitPhase, idleSeconds;
        private double previousTime;
        private Vector3 previousPosition;
        private bool hasFrame;

        internal sealed class Palette
        {
            public Material Shell = null!, Joints = null!;
        }

        public static Palette CreatePalette(List<Material> owned) => new Palette {
            Shell = Material(owned, new Color(.78f, .80f, .77f)),
            Joints = Material(owned, new Color(.12f, .15f, .17f))
        };

        public static DevelopmentHumanoidVisual Create(Transform parent, Palette palette, DevelopmentMotionSettings settings,double floorY)
        {
            var prefab = Resources.Load<GameObject>(ModelResource);
            if (prefab == null) throw new InvalidOperationException("CC0 mannequin model is missing.");
            var root = new GameObject("Synthetic training mannequin");
            root.transform.SetParent(parent, false);
            root.layer = DevelopmentView.StimulusLayer;
            var actor = root.AddComponent<DevelopmentHumanoidVisual>();
            actor.settings = settings;
            actor.floorY = floorY;
            actor.model = Instantiate(prefab, root.transform, false);
            actor.model.name = "Quaternius mannequin";
            foreach (var part in actor.model.GetComponentsInChildren<Transform>(true))
                part.gameObject.layer = DevelopmentView.StimulusLayer;
            // Clips are sampled explicitly so they cannot move the scenario root or
            // continue playing when the station freezes a target.
            foreach (var animation in actor.model.GetComponentsInChildren<Animation>())
                animation.enabled = false;
            foreach (var animator in actor.model.GetComponentsInChildren<Animator>())
                animator.enabled = false;
            foreach (var collider in actor.model.GetComponentsInChildren<Collider>())
            { collider.enabled = false; Destroy(collider); }
            foreach (var renderer in actor.model.GetComponentsInChildren<Renderer>())
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = materials[i] != null && materials[i].name.Contains("Joints")
                        ? palette.Joints : palette.Shell;
                renderer.sharedMaterials = materials;
            }
            actor.animationPlayer = actor.model.GetComponent<Animation>() ?? actor.model.AddComponent<Animation>();
            actor.animationPlayer.enabled = false;
            actor.animationPlayer.playAutomatically = false;
            actor.animationPlayer.cullingType = AnimationCullingType.AlwaysAnimate;
            // Instantiation can start the FBX's default clip before we disable the
            // component. Remove that hidden contribution from our explicit blend.
            actor.animationPlayer.Stop();
            foreach (AnimationState state in actor.animationPlayer) state.enabled = false;
            foreach (var clip in Resources.LoadAll<AnimationClip>(ModelResource))
                if (!clip.name.StartsWith("__preview__")) actor.animationPlayer.AddClip(clip, ClipName(clip));
            actor.idle = actor.Clip("Idle_Loop");
            actor.walk = actor.Clip("Walk_Loop");
            actor.jog = actor.Clip("Jog_Fwd_Loop");
            actor.crouchIdle = actor.Clip("Crouch_Idle_Loop");
            actor.crouchWalk = actor.Clip("Crouch_Fwd_Loop");
            actor.idle.weight = 1;
            actor.animationPlayer.Sample();
            actor.FitStandingModel();
            actor.model.transform.localPosition = actor.fittedPosition;
            actor.model.transform.localScale = actor.fittedScale;
            foreach (var bone in actor.model.GetComponentsInChildren<Transform>())
            {
                if (bone.name == "foot_l") actor.leftFoot = bone;
                if (bone.name == "foot_r") actor.rightFoot = bone;
            }
            if (actor.leftFoot == null || actor.rightFoot == null)
                throw new InvalidOperationException("Mannequin foot bones are missing.");
            actor.standingFootY = actor.LowestFootY();
            return actor;
        }

        private AnimationState Clip(string name)
        {
            var state = animationPlayer[name];
            if (state == null) throw new InvalidOperationException("Mannequin clip missing: " + name);
            state.enabled = true;
            state.weight = 0;
            state.speed = 0; // Only active session time advances the manually sampled blend.
            state.wrapMode = WrapMode.Loop;
            return state;
        }

        private static string ClipName(AnimationClip clip) => clip.name.Substring(clip.name.LastIndexOf('|') + 1);

        private void FitStandingModel()
        {
            // Fit in virtual metres to the existing anatomy, rather than changing
            // hit scoring to match the source asset's export units.
            var bounds = new Bounds();
            bool first = true;
            foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = new Mesh();
                renderer.BakeMesh(mesh);
                foreach (var vertex in mesh.vertices)
                {
                    var point = transform.InverseTransformPoint(renderer.transform.TransformPoint(vertex));
                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                    else bounds.Encapsulate(point);
                }
                Destroy(mesh);
            }
            if (first || bounds.size.y <= 0) throw new InvalidOperationException("Mannequin mesh has no height.");
            float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
            foreach (var part in HumanoidGeometry.Parts(new Vector3d(0, HumanoidGeometry.StandingCenterY, 0)))
            {
                float localY = (float)(part.Center.Y - HumanoidGeometry.StandingCenterY);
                bottom = Mathf.Min(bottom, localY - (float)part.Radii.Y);
                top = Mathf.Max(top, localY + (float)part.Radii.Y);
            }
            float scale = (top - bottom) / bounds.size.y;
            fittedScale = model.transform.localScale * scale;
            fittedPosition = new Vector3(-bounds.center.x * scale, bottom - bounds.min.y * scale, -bounds.center.z * scale);
        }

        /// <summary>Follow the authoritative position without root motion. Repeated
        /// timestamps preserve the exact pose, including during session pauses.</summary>
        public void Pose(Vector3 position, double activeSeconds)
        {
            double elapsed = hasFrame ? activeSeconds - previousTime : 0;
            if (hasFrame && elapsed == 0 && position == previousPosition) return;
            Vector3 travel = position - previousPosition;
            travel.y = 0;
            // A seek/new attempt is a new visual sample, never a sprint between observations.
            bool discontinuity = !hasFrame || elapsed < 0 || elapsed > .25 || travel.magnitude > 1;
            float dt = discontinuity ? 0 : (float)elapsed;
            if (discontinuity) { speed = 0; yaw = 0; gaitPhase = 0; idleSeconds = 0; }
            float measuredSpeed = dt > 0 ? travel.magnitude / dt : 0;
            float blend = dt > 0 ? 1 - Mathf.Exp(-dt / Mathf.Max(.01f, settings.blendSeconds)) : 1;
            speed = Mathf.Lerp(speed, measuredSpeed, blend);
            float standingCenterY = (float)HumanoidGeometry.StandingCenterAt(floorY);
            float crouch = Mathf.Clamp01((standingCenterY - position.y) /
                (float)(HumanoidGeometry.StandingCenterY - HumanoidGeometry.CrouchedCenterY));
            float locomotion = Mathf.SmoothStep(0, 1, speed / Mathf.Max(.1f, settings.walkSpeedMps * .55f));
            float run = Mathf.InverseLerp(settings.walkSpeedMps, Mathf.Max(settings.walkSpeedMps + .1f, settings.jogSpeedMps), speed);
            // Share the left/right foot phase across gaits; actual distance sets cadence.
            float stride = Mathf.Lerp(settings.walkSpeedMps * walk.length, settings.jogSpeedMps * jog.length, run);
            stride = Mathf.Lerp(stride, settings.crouchSpeedMps * crouchWalk.length, crouch);
            if (dt > 0) gaitPhase = Mathf.Repeat(gaitPhase + travel.magnitude / Mathf.Max(.1f, stride), 1);
            idleSeconds += dt;
            float targetYaw = measuredSpeed > .04f ? Mathf.Atan2(-travel.x, -travel.z) * Mathf.Rad2Deg : 0;
            yaw = Mathf.MoveTowardsAngle(yaw, targetYaw, settings.turnDegreesPerSecond * dt);
            SetState(idle, (1 - crouch) * (1 - locomotion), idleSeconds / idle.length);
            SetState(walk, (1 - crouch) * locomotion * (1 - run), gaitPhase);
            SetState(jog, (1 - crouch) * locomotion * run, gaitPhase);
            SetState(crouchIdle, crouch * (1 - locomotion), idleSeconds / crouchIdle.length);
            SetState(crouchWalk, crouch * locomotion, gaitPhase);
            ApplySample(position, yaw);
            previousTime = activeSeconds;
            previousPosition = position;
            hasFrame = true;
        }

        /// <summary>A fixed target holds an exact clip sample, even while session time advances.</summary>
        public void PoseFixed(Vector3 position, DevelopmentFixedPose pose)
        {
            float stride = pose.MidStride ? 1 : 0;
            SetState(idle, (1 - pose.Crouch) * (1 - stride), pose.Phase);
            SetState(walk, (1 - pose.Crouch) * stride, pose.Phase);
            SetState(jog, 0, 0);
            SetState(crouchIdle, pose.Crouch * (1 - stride), pose.Phase);
            SetState(crouchWalk, pose.Crouch * stride, pose.Phase);
            ApplySample(position, pose.YawDegrees);
            // If this actor is reused by a moving block, start its motion history afresh.
            hasFrame = false;
        }

        private void ApplySample(Vector3 position, float facingDegrees)
        {
            animationPlayer.Sample();
            transform.position = position;
            transform.localScale = Vector3.one;
            // Only the visual child turns. Logged target coordinates and hit geometry stay authoritative.
            model.transform.localRotation = Quaternion.Euler(0, facingDegrees, 0);
            model.transform.localPosition = fittedPosition;
            model.transform.localScale = fittedScale;
            // Bend at the hips/knees instead of compressing the mesh. Compensate for
            // the scenario centre lowering so the supporting foot stays on the floor.
            float correction = standingFootY + (float)HumanoidGeometry.StandingCenterAt(floorY) - position.y - LowestFootY();
            model.transform.localPosition += Vector3.up * correction;
        }

        private float LowestFootY() => Mathf.Min(transform.InverseTransformPoint(leftFoot.position).y,
            transform.InverseTransformPoint(rightFoot.position).y);

        private static void SetState(AnimationState state, float weight, float phase)
        { state.weight = weight; state.normalizedTime = Mathf.Repeat(phase, 1); }

        public static void CreateCovers(Transform parent,IReadOnlyList<HumanoidCover> covers,List<Material> owned)
        {
            var furniture=new LibraryInterior(owned);
            foreach(var cover in covers)
            {
                var host=new GameObject("Cover · "+cover.Id);host.layer=DevelopmentView.StimulusLayer;
                host.transform.SetParent(parent,false);
                host.transform.position=new Vector3((float)cover.Center.X,(float)cover.Center.Y,(float)cover.Center.Z);
                var full=new Vector3((float)cover.HalfSize.X*2,(float)cover.HalfSize.Y*2,(float)cover.HalfSize.Z*2);
                if(cover.Id.StartsWith("low-bookcase"))
                {
                    // Closed backing preserves the authoritative cover silhouette; shelf interiors
                    // and individually bound books add depth on the participant-facing side.
                    int slot=int.Parse(cover.Id.Substring(cover.Id.LastIndexOf('-')+1));
                    var baseRoot=LibraryInterior.Group(host.transform,"Bookcase construction",new Vector3(0,-full.y/2,0));
                    furniture.Bookcase(baseRoot,full.x,full.y,full.z,slot);
                }
                else
                    LibraryInterior.Box(host.transform,"Desk construction",Vector3.zero,full,
                        cover.Id.StartsWith("desk-leg")?furniture.Metal:furniture.Oak);
            }
        }

        private static Material Material(List<Material> owned,Color color)
        {
            var template=Resources.Load<Material>("ELTS/TrainingRoomSurface");
            if(template==null)throw new InvalidOperationException("Training room surface material is missing.");
            var result=new Material(template);result.SetColor("_BaseColor",color);owned.Add(result);return result;
        }
        private static GameObject Part(Transform parent,string name,PrimitiveType shape,Vector3 position,Vector3 scale,Material material)
        {
            var part=GameObject.CreatePrimitive(shape);part.name=name;part.layer=DevelopmentView.StimulusLayer;
            part.transform.SetParent(parent,false);part.transform.localPosition=position;part.transform.localScale=scale;
            var collider=part.GetComponent<Collider>();collider.enabled=false;Destroy(collider);
            part.GetComponent<Renderer>().sharedMaterial=material;return part;
        }
    }
}
