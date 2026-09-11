using System.Collections;
using System.IO;
using UnityEngine;
using PeakMapInteractive.Tracker;

namespace PeakMapInteractive.Automation
{
    /// <summary>
    /// Stands the device in front of the camera, photographs it from four
    /// sides, and quits — with nobody at the keyboard.
    ///
    /// The same bargain the icon run struck, for the same reason. Everything
    /// that can go wrong between a model in Blender and a model in this game
    /// goes wrong invisibly: a mesh converted with the wrong handedness renders
    /// inside out and simply looks a bit odd; a palette sampled with the
    /// texture origin flipped picks real colours, just the wrong ones; a
    /// missing shader property is ignored in silence. None of that is
    /// reasoned out. All of it is obvious in a photograph.
    ///
    /// So the loop is a minute long and has no person in it.
    /// </summary>
    internal static class TrackerRun
    {
        internal static bool HasCompleted { get; private set; }

        /// <summary>How far in front of the camera the device is stood.</summary>
        private const float Distance = 1.0f;

        /// <summary>
        /// Photographed larger than life. At its true 12 cm it would be a
        /// smudge at any distance the near clip plane allows, and what these
        /// pictures are for is the geometry and the colours, not the scale —
        /// which is a number, and is in the log.
        /// </summary>
        private const float Magnify = 5f;

        /// <summary>
        /// Whether to stand a second device beside the first, drawn by the
        /// other shader. Off: the choice has been made.
        /// </summary>
        private const bool Compare = false;

        /// <summary>How far to either side of the view the two versions stand.</summary>
        private const float Apart = 0.4f;

        private static readonly float[] Angles = { 0f, 90f, 180f, 270f };

        internal static IEnumerator Run()
        {
            HasCompleted = true;

            try
            {
                LoadingScreenHandler.KillCurrentLoadingScreen();
            }
            catch (System.Exception error)
            {
                Plugin.Logger.LogWarning($"Tracker run: could not dismiss the loading screen: {error.Message}");
            }

            yield return new WaitForSecondsRealtime(Plugin.Settings.MinimapStartDelay.Value);

            Report();
            Tracker.TrackerObject.ProbeShaders();
            Tracker.Sounds.Verify();
            ReportGrips();
            ReportItem();

            yield return LightTheScreen();


            // A second device, drawn by the other shader, when there is a
            // choice being made.
            //
            // There was one, and it is made: PEAK's own shader against the
            // pipeline's stock lit one, which is the only one that can read the
            // material mask. Photographed side by side on the beach, the
            // pipeline's turned the case olive and the bezel deep blue - the
            // mask working exactly as designed, and a smoothness of 0.78
            // reflecting a tropical sky. Left in because the same question will
            // come back the first time somebody wants wet-looking plastic, and
            // answering it again should cost one run rather than an afternoon.
            GameObject left = TrackerObject.Build("HikingGPS_Preview");
            GameObject right = Compare
                ? TrackerObject.Build("HikingGPS_Preview_Alt", "Universal Render Pipeline/Lit")
                : null;

            if (left == null)
            {
                Plugin.Logger.LogError("Tracker run: nothing to photograph.");
                yield return Leave();
                yield break;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                Plugin.Logger.LogError("Tracker run: there is no main camera to stand in front of.");
                Object.Destroy(left);
                Object.Destroy(right);
                yield return Leave();
                yield break;
            }

            Plugin.Logger.LogInfo(
                $"Tracker run: camera at {camera.transform.position}, looking {camera.transform.forward}.");

            left.transform.localScale = Vector3.one * Magnify;
            if (right != null) right.transform.localScale = Vector3.one * Magnify;

            // Nothing has to be hidden first: under AutoRun the minimap is
            // never built at all, so the view is the game's and nothing else.
            string folder = Path.Combine(Plugin.OutputDir, "tracker");
            Directory.CreateDirectory(folder);

            foreach (float angle in Angles)
            {
                Place(left, camera, angle, right == null ? 0f : -Apart);
                if (right != null) Place(right, camera, angle, Apart);

                yield return Shoot(Path.Combine(folder, $"tracker-{angle:000}.png"));
            }

            // One more under a lamp of its own. If the run happens to have put
            // the camera inside the terrain, the four above are pictures of the
            // dark, and this is the one that still says whether the mesh is
            // right.
            GameObject lamp = Lamp(camera);

            Place(left, camera, 35f, right == null ? 0f : -Apart);
            if (right != null) Place(right, camera, 35f, Apart);

            yield return Shoot(Path.Combine(folder, "tracker-lit.png"));

            Object.Destroy(lamp);
            Object.Destroy(left);
            if (right != null) Object.Destroy(right);

            Plugin.Logger.LogInfo($"Tracker run: wrote {Angles.Length + 1} photographs to {folder}");

            // And then the picture that actually decides the grip points: a
            // character holding the thing. Quietly does nothing when the device
            // is not a registered item, which is the state everything above
            // still works in.
            yield return HandShot.Run(folder);

            // And how it is found: laid in the nearest suitcase, seen from
            // above and from the front.
            yield return LuggageShot.Run(folder);

            // And on a backpack, one in every slot.
            yield return BackpackShot.Run(folder);

            yield return Leave();
        }

        /// <summary>
        /// Brings the map up as a device screen and puts it on the glass.
        ///
        /// Under AutoRun the map is never built — it is not safe to play under
        /// automation, and the automation is what is running. So one is built
        /// here on purpose, in the mode where it renders into a texture rather
        /// than into a corner of the screen, and told to draw regardless of
        /// whether the character is standing up yet.
        /// </summary>
        private static IEnumerator LightTheScreen()
        {
            Minimap.MinimapController.OnDevice = true;
            Minimap.MinimapController.ForceVisible = true;

            Plugin.Instance.gameObject.AddComponent<Minimap.MinimapController>();

            // A moment for the camera to render a frame into the texture, and
            // for the marker scan to find anything nearby worth drawing.
            yield return new WaitForSecondsRealtime(2f);

            Texture screen = Minimap.MinimapController.DeviceScreen;

            if (screen == null)
            {
                Plugin.Logger.LogWarning("Tracker run: the device screen was never built.");
                yield break;
            }

            TrackerObject.ShowMap(screen);
        }

        /// <summary>
        /// What was loaded, in numbers, so a photograph that looks wrong can be
        /// checked against the thing it was made from without another run.
        /// </summary>
        private static void Report()
        {
            if (!TrackerModel.Available)
            {
                Plugin.Logger.LogError("Tracker run: the model did not load.");
                return;
            }

            foreach (TrackerModel.Part part in TrackerModel.Parts)
            {
                Bounds bounds = part.Mesh.bounds;

                Plugin.Logger.LogInfo(
                    $"Tracker: {part.Name} [{part.Material}] " +
                    $"{part.Mesh.vertexCount} verts, {part.Mesh.triangles.Length / 3} tris, " +
                    $"offset {part.Offset}, centre {bounds.center}, size {bounds.size}");
            }
        }

        /// <summary>
        /// Whether the device became a real item, and how often it will turn up.
        ///
        /// Registering is three separate things that can each half-succeed: the
        /// item exists, the item is in the game's database under an id, and the
        /// loot tables have noticed it. Only the last one is what a player
        /// experiences, and none of the three announces itself.
        /// </summary>
        private static void ReportItem()
        {
            if (!Plugin.Settings.TrackerAsItem.Value)
            {
                Plugin.Logger.LogInfo("Item: turned off in the config.");
                return;
            }

            if (!Plugin.HasPeakLib)
            {
                Plugin.Logger.LogInfo("Item: PEAKLib is not installed.");
                return;
            }

            if (!Tracker.TrackerItem.Registered)
            {
                Plugin.Logger.LogWarning("Item: the device was never registered.");
                return;
            }

            Item item = Tracker.TrackerItem.Prefab.GetComponent<Item>();
            Plugin.Logger.LogInfo($"Item: '{Tracker.TrackerItem.Prefab.name}' has id {item.itemID}.");

            bool inDatabase = ItemDatabase.TryGetItem(item.itemID, out Item found) && found == item;
            ItemDatabase database = Zorro.Core.SingletonAsset<ItemDatabase>.Instance;

            Plugin.Logger.LogInfo(
                $"Item: in the database: {inDatabase} " +
                $"(the database holds {(database?.itemLookup == null ? -1 : database.itemLookup.Count)}).");

            foreach (Transform hand in new[]
                     {
                         Tracker.TrackerItem.Prefab.transform.Find("Hand_L"),
                         Tracker.TrackerItem.Prefab.transform.Find("Hand_R")
                     })
            {
                Plugin.Logger.LogInfo(
                    hand == null
                        ? "Item: a grip point is MISSING - picking it up would throw."
                        : $"Item: {hand.name} at {hand.localPosition.ToString("F4")} " +
                          $"euler {hand.localEulerAngles.ToString("F1")}");
            }

            // Only once it is actually in there. An unregistered item still has
            // id 0, and 0 belongs to somebody: asking the loot tables about it
            // returns a real answer about a different item entirely, which is
            // worse than no answer because it looks like success.
            if (!inDatabase) return;

            try
            {
                LootData.PopulateLootData();

                foreach (SpawnPool pool in System.Enum.GetValues(typeof(SpawnPool)))
                {
                    if (pool == SpawnPool.None) continue;

                    float odds = LootData.GetPercentageOdds(item.itemID, pool);
                    if (odds > 0f) Plugin.Logger.LogInfo($"Item: {odds:0.0}% of {pool}.");
                }
            }
            catch (System.Exception error)
            {
                Plugin.Logger.LogWarning($"Item: could not read the loot tables: {error.Message}");
            }
        }

        /// <summary>
        /// How the game's own items tell it where the hands go.
        ///
        /// A held item is not parented to a hand. <c>CharacterItems</c> looks up
        /// two children by name — <c>item.transform.Find("Hand_L")</c> and
        /// <c>"Hand_R"</c> — moves the hand rigs onto them and welds both to the
        /// item with a <c>FixedJoint</c>, unless the item asks for one hand only.
        /// So the device needs two empties with exactly those names, and their
        /// rotation matters as much as their position: it is what sets the
        /// angle of the wrist.
        ///
        /// Which way those rotations are meant to face is written down nowhere
        /// and cannot be read out of the decompiled code, because it lives in
        /// the prefabs. It can be measured, though, off any item lying on the
        /// beach — which is what this does, so that the grip points can be
        /// drawn to a convention that is known rather than guessed.
        /// </summary>
        private static void ReportGrips()
        {
            ItemDatabase database = Zorro.Core.SingletonAsset<ItemDatabase>.Instance;

            if (database?.itemLookup == null)
            {
                Plugin.Logger.LogWarning("Grip: there is no item database to read.");
                return;
            }

            int withGrips = 0;
            int shown = 0;

            foreach (var entry in database.itemLookup)
            {
                Item item = entry.Value;
                if (item == null) continue;

                Transform left = item.transform.Find("Hand_L");
                Transform right = item.transform.Find("Hand_R");

                if (left == null && right == null) continue;

                withGrips++;
                if (shown >= 30) continue;
                shown++;

                Plugin.Logger.LogInfo(
                    $"Grip: {item.name} rightHandOnly={item.rightHandOnly} size={Extent(item).ToString("F3")}");

                if (left != null)
                    Plugin.Logger.LogInfo(
                        $"Grip:   Hand_L {left.localPosition.ToString("F4")} euler {left.localEulerAngles.ToString("F1")}");

                if (right != null)
                    Plugin.Logger.LogInfo(
                        $"Grip:   Hand_R {right.localPosition.ToString("F4")} euler {right.localEulerAngles.ToString("F1")}");
            }

            Plugin.Logger.LogInfo(
                $"Grip: {withGrips} of {database.itemLookup.Count} item(s) carry grip points; showed {shown}.");
        }

        /// <summary>
        /// How big an item is, from its meshes rather than its renderers.
        ///
        /// These are prefabs and never enabled, and a renderer that has never
        /// been drawn reports bounds of nothing — which would make every item
        /// in the database look like a point.
        /// </summary>
        private static Vector3 Extent(Item item)
        {
            var bounds = new Bounds();
            bool any = false;

            foreach (MeshFilter filter in item.GetComponentsInChildren<MeshFilter>(includeInactive: true))
            {
                if (filter == null || filter.sharedMesh == null) continue;

                Bounds local = filter.sharedMesh.bounds;
                Vector3 centre = item.transform.InverseTransformPoint(filter.transform.TransformPoint(local.center));

                if (!any)
                {
                    bounds = new Bounds(centre, local.size);
                    any = true;
                    continue;
                }

                bounds.Encapsulate(new Bounds(centre, local.size));
            }

            return any ? bounds.size : Vector3.zero;
        }

        /// <summary>
        /// In front of the camera, facing it, turned by the given angle.
        ///
        /// Aimed at the camera rather than levelled against the world. The
        /// first version flattened the camera's forward to get a heading, which
        /// works right up until the run spawns the character looking at its own
        /// feet — and then the heading is nothing at all, the fallback points
        /// the device at the horizon, and every photograph is of its edge. What
        /// these pictures are for is the object, so the object is pointed at
        /// the lens and stood up in the frame.
        /// </summary>
        private static void Place(GameObject device, Camera camera, float angle, float across = 0f)
        {
            Transform eye = camera.transform;

            device.transform.position =
                eye.position + eye.forward * Distance + eye.right * across;

            // The screen is on the model's +Z, so a device whose +Z points back
            // at the lens is a device being read.
            Vector3 toCamera = eye.position - device.transform.position;

            device.transform.rotation =
                Quaternion.LookRotation(toCamera.normalized, eye.up) * Quaternion.Euler(0f, angle, 0f);
        }

        /// <summary>A light that belongs to nobody, so the object is never a silhouette.</summary>
        private static GameObject Lamp(Camera camera)
        {
            var holder = new GameObject("HikingGPS_PreviewLamp");
            holder.transform.position = camera.transform.position;
            holder.transform.rotation = Quaternion.LookRotation(camera.transform.forward);

            Light light = holder.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color = Color.white;
            light.shadows = LightShadows.None;

            return holder;
        }

        private static IEnumerator Shoot(string path)
        {
            // A frame for the move to be drawn before it is photographed.
            yield return null;
            yield return new WaitForEndOfFrame();

            try
            {
                ScreenCapture.CaptureScreenshot(path);
            }
            catch (System.Exception error)
            {
                Plugin.Logger.LogWarning($"Tracker run: could not photograph: {error.Message}");
                yield break;
            }

            // The capture lands on a later frame, and quitting before it does
            // leaves a zero-byte file.
            yield return new WaitForSecondsRealtime(1.5f);
        }

        private static IEnumerator Leave()
        {
            if (!Plugin.Settings.QuitWhenDone.Value) yield break;

            yield return new WaitForSecondsRealtime(1f);
            Application.Quit();
        }
    }
}
