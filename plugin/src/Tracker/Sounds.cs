using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace PeakMapInteractive.Tracker
{
    /// <summary>
    /// The noises the device makes: waking, sleeping, and its buttons.
    ///
    /// Drawn from six short clips built into the assembly beside the artwork
    /// and the model. They are WAV rather than anything compressed for one
    /// reason: a WAV can be turned into an <see cref="AudioClip"/> from a byte
    /// array in a few lines, where every compressed format in this engine wants
    /// a file on disk and an asynchronous load — a second failure to handle in
    /// exchange for saving eighty kilobytes.
    ///
    /// They are mono, and that is not a preference either. Once the device is
    /// an object somebody is holding, its sound is positional, and the engine
    /// mixes a mono clip into stereo from where the object is relative to the
    /// listener. A stereo clip cannot be placed at all: a partner twenty metres
    /// down the ridge would hear the button as loudly, and from the same
    /// direction, as the person pressing it.
    /// </summary>
    internal static class Sounds
    {
        private static readonly Dictionary<string, AudioClip> _clips =
            new Dictionary<string, AudioClip>();

        private static AudioSource _source;
        private static int _nextClick;

        /// <summary>
        /// Where the sound comes from, or null while it should stay silent.
        ///
        /// Null covers two cases that both want silence and neither of which is
        /// an error: a volume turned down to nothing, and the automated runs,
        /// which mute the game wholesale so a scheduled capture does not
        /// announce itself from an off-screen window.
        /// </summary>
        private static AudioSource Source()
        {
            if (Volume <= 0f) return null;
            if (_source != null) return _source;

            Plugin plugin = Plugin.Instance;
            if (plugin == null) return null;

            _source = plugin.gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;

            // Flat, because this is the map in the corner of the screen rather
            // than a thing in the world. When the device becomes an item in
            // somebody's hands it gets its own source, positioned on it.
            _source.spatialBlend = 0f;

            return _source;
        }

        private static float Volume => Mathf.Clamp01(Plugin.Settings.SoundVolume.Value);

        // --- the noises ------------------------------------------------------

        internal static void PowerOn() => Play("power_on");

        internal static void PowerOff() => Play("power_off");

        /// <summary>
        /// One button press.
        ///
        /// Three clips, taken in turn. One would do technically, and sounds
        /// like a machine the moment somebody holds the zoom key: six presses
        /// of a single sample is six identical waveforms, and the ear hears the
        /// repetition rather than the button.
        /// </summary>
        internal static void Click()
        {
            Play(_nextClick == 0 ? "click_a" : _nextClick == 1 ? "click_b" : "click_c");
            _nextClick = (_nextClick + 1) % 3;
        }

        /// <summary>The end of the zoom ladder: a dull knock, not an error.</summary>
        internal static void ZoomLimit() => Play("zoom_limit");

        /// <summary>
        /// Decodes every clip and says what came out.
        ///
        /// The automated runs mute the game, so they cannot tell anybody
        /// whether a sound is any good — but they can tell whether it exists,
        /// which is the half that fails silently. A WAV read with the chunk
        /// walk gone wrong still produces an AudioClip; it is just the wrong
        /// length, or noise, and nothing reports it.
        /// </summary>
        internal static void Verify()
        {
            foreach (string name in new[]
                     { "power_on", "power_off", "click_a", "click_b", "click_c", "zoom_limit" })
            {
                AudioClip clip = Load(name);

                if (clip == null)
                {
                    Plugin.Logger.LogWarning($"Sounds: '{name}' did not load.");
                    continue;
                }

                Plugin.Logger.LogInfo(
                    $"Sounds: {name} {clip.length * 1000f:0} ms, " +
                    $"{clip.channels} ch, {clip.frequency} Hz, {clip.samples} samples.");
            }
        }

        private static void Play(string name)
        {
            AudioSource source = Source();
            if (source == null) return;

            AudioClip clip = Load(name);
            if (clip == null) return;

            source.PlayOneShot(clip, Volume);
        }

        // --- loading ---------------------------------------------------------

        private static AudioClip Load(string name)
        {
            if (_clips.TryGetValue(name, out AudioClip cached)) return cached;

            _clips[name] = null;

            byte[] wav = Resource(name + ".wav");
            if (wav == null)
            {
                Plugin.Logger.LogWarning($"Sounds: '{name}.wav' is not in the assembly.");
                return null;
            }

            try
            {
                _clips[name] = Decode(name, wav);
            }
            catch (System.Exception error)
            {
                Plugin.Logger.LogWarning($"Sounds: '{name}.wav' would not decode — {error.Message}");
            }

            return _clips[name];
        }

        /// <summary>
        /// A 16-bit PCM WAV, walked chunk by chunk.
        ///
        /// Chunks are walked rather than assumed at fixed offsets because an
        /// encoder is free to put LIST, fact or anything else between the
        /// header and the samples, and a reader that seeks to byte 44 gets
        /// metadata read as audio — which does not throw, it just plays as
        /// noise.
        /// </summary>
        private static AudioClip Decode(string name, byte[] wav)
        {
            using (var stream = new MemoryStream(wav, writable: false))
            using (var reader = new BinaryReader(stream))
            {
                if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF")
                    throw new IOException("not a RIFF file");

                reader.ReadInt32();

                if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE")
                    throw new IOException("not a WAVE file");

                int channels = 0;
                int rate = 0;
                int bits = 0;
                byte[] samples = null;

                while (stream.Position + 8 <= stream.Length)
                {
                    string id = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    int size = reader.ReadInt32();

                    if (id == "fmt ")
                    {
                        int format = reader.ReadInt16();
                        channels = reader.ReadInt16();
                        rate = reader.ReadInt32();
                        reader.ReadInt32();     // byte rate
                        reader.ReadInt16();     // block align
                        bits = reader.ReadInt16();

                        if (format != 1) throw new IOException($"format {format} is not plain PCM");

                        // Chunks are padded to an even length, and the header
                        // may carry extension bytes past what is read here.
                        stream.Position += size - 16 + (size & 1);
                        continue;
                    }

                    if (id == "data")
                    {
                        samples = reader.ReadBytes(size);
                        stream.Position += size & 1;
                        continue;
                    }

                    stream.Position += size + (size & 1);
                }

                if (samples == null) throw new IOException("no data chunk");
                if (bits != 16) throw new IOException($"{bits}-bit samples; 16 expected");

                int count = samples.Length / 2;
                var data = new float[count];

                for (int i = 0; i < count; i++)
                    data[i] = (short)(samples[i * 2] | (samples[i * 2 + 1] << 8)) / 32768f;

                AudioClip clip = AudioClip.Create(
                    "HikingGPS_" + name, count / channels, channels, rate, stream: false);

                clip.SetData(data, 0);
                return clip;
            }
        }

        private static byte[] Resource(string name)
        {
            Assembly assembly = typeof(Sounds).Assembly;

            foreach (string candidate in assembly.GetManifestResourceNames())
            {
                if (!candidate.EndsWith(name, System.StringComparison.OrdinalIgnoreCase)) continue;

                using (Stream stream = assembly.GetManifestResourceStream(candidate))
                {
                    if (stream == null) return null;

                    var blob = new byte[stream.Length];
                    int read = 0;

                    while (read < blob.Length)
                    {
                        int got = stream.Read(blob, read, blob.Length - read);
                        if (got <= 0) break;
                        read += got;
                    }

                    return blob;
                }
            }

            return null;
        }
    }
}
