using UnityEngine;

namespace Elts.Operator
{
    /// <summary>Scenic window bays and the courtyard beyond the library's closed glazing.</summary>
    internal static class LibraryWindows
    {
        // (centre Z, clear width), in room metres. Each bay fits between existing side doors.
        private static readonly Vector2[] EastBays = { new(7, 5.3f), new(17.8f, 6.3f), new(26.4f, 2.65f) };
        internal const string LightGroup = "Reading area window light";

        public static void EastWall(Transform room, string name, Vector3 center, Vector3 size,
            LibraryInterior furniture, Material plaster)
        {
            foreach (var bay in EastBays)
            {
                if (Mathf.Abs(bay.x - center.z) + bay.y / 2 > size.z / 2) continue;
                // Local X runs towards decreasing room Z on this wall; local Z faces outside.
                var wall = LibraryInterior.Group(room, name, new Vector3(center.x, 0, center.z), 90);
                WindowWall(wall, size.z, size.y, center.z - bay.x, bay.y, .78f, 3.22f, furniture, plaster);
                return;
            }
            LibraryInterior.Box(room, name, center, size, plaster, false);
        }

        public static void RearWall(Transform room, float z, float width, float height,
            LibraryInterior furniture, Material plaster)
        {
            var wall = LibraryInterior.Group(room, "Far wall", new Vector3(0, 0, z));
            // A high return above the back bookcases makes the courtyard visible from the central aisle.
            WindowWall(wall, width, height, 6.25f, 8.2f, 2.43f, 3.46f, furniture, plaster);
        }

        private static void WindowWall(Transform wall, float width, float height, float center, float opening,
            float sill, float head, LibraryInterior furniture, Material plaster)
        {
            float left = center - opening / 2, right = center + opening / 2;
            void Panel(string name, float from, float to, float bottom, float top)
            {
                LibraryInterior.Box(wall, name, new Vector3((from+to)/2, (bottom+top)/2, 0),
                    new Vector3(to-from, top-bottom, .24f), plaster, false);
            }
            Panel("Wall pier", -width / 2, left, 0, height);
            Panel("Wall pier", right, width / 2, 0, height);
            Panel("Under window", left, right, 0, sill);
            Panel("Over window", left, right, head, height);

            float middleY = (head + sill) / 2;
            foreach (float edge in new[] {left, right})
                LibraryInterior.Box(wall, "Recessed oak jamb", new Vector3(edge, middleY, -.03f),
                    new Vector3(.085f, head - sill, .36f), furniture.Oak);
            LibraryInterior.Box(wall, "Window sill", new Vector3(center, sill, -.09f),
                new Vector3(opening + .16f, .075f, .49f), furniture.Oak);
            LibraryInterior.Box(wall, "Window head", new Vector3(center, head, -.03f),
                new Vector3(opening, .085f, .36f), furniture.Oak);
            int panes = Mathf.CeilToInt(opening / 1.8f);
            for (int i = 0; i <= panes; i++)
                LibraryInterior.Box(wall, "Glazing bar", new Vector3(left + opening*i/panes, middleY, .075f),
                    new Vector3(.045f, head - sill, .08f), furniture.Metal, false);
        }

        public static void Courtyard(Transform room, LibraryInterior furniture, TrainingRoomSettings settings)
        {
            var court = LibraryInterior.Group(room, "Library courtyard", Vector3.zero);
            var paving = furniture.Surface(new Color(.69f,.71f,.67f));
            var masonry = furniture.Surface(new Color(.77f,.66f,.52f), .16f, "white_plaster_02", new Vector2(9,2));
            var coping = furniture.Surface(new Color(.85f,.82f,.73f));
            void Box(string name, Vector3 at, Vector3 size, Material material)
                => LibraryInterior.Box(court, name, at, size, material, false);

            Box("East courtyard paving", new Vector3(22,-.1f,22), new Vector3(22,.16f,64), paving);
            Box("Rear courtyard paving", new Vector3(0,-.1f,41), new Vector3(22,.16f,26), paving);
            Box("East garden wall", new Vector3(27,1.07f,25), new Vector3(.36f,2.14f,66), masonry);
            Box("East wall coping", new Vector3(27,2.19f,25), new Vector3(.46f,.10f,66), coping);
            Box("Rear garden wall", new Vector3(8,1.53f,49), new Vector3(38,3.06f,.36f), masonry);
            Box("Rear wall coping", new Vector3(8,3.11f,49), new Vector3(38,.10f,.46f), coping);
            var trees = new[] {new Vector3(14,0,15),new Vector3(17,0,27),new Vector3(14,0,39),
                new Vector3(23,0,35),new Vector3(8.1f,0,38),new Vector3(.8f,0,43),new Vector3(14.8f,0,46)};
            var foliage = furniture.PaintedSurface("library-palette", .12f);
            foliage.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            for (int i = 0; i < trees.Length; i++)
            {
                float scale = .88f + (i % 3)*.14f;
                var tree = furniture.Model(court, "Courtyard tree", "CourtyardTree", trees[i], Vector3.one*scale, foliage);
                tree.transform.localRotation = Quaternion.Euler(0, i*73, 0);
            }

            var lights = LibraryInterior.Group(room, LightGroup, Vector3.zero);
            foreach (var bay in EastBays)
            {
                // A broad, shadowless fill represents sky bounce beside each opening.
                var host = LibraryInterior.Group(lights, "Window bounce", new Vector3(9.25f,2.8f,bay.x));
                var light = host.gameObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = settings.windowBounce;
                light.intensity = settings.windowIntensity * (bay.y / 6.3f);
                light.range = settings.windowReach * room.localScale.x;
                light.shadows = LightShadows.None;
                light.cullingMask = 1 << DevelopmentView.StimulusLayer;
            }
        }
    }
}
