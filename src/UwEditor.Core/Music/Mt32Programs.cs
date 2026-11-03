using System;

namespace UwEditor.Core.Music
{
    /// <summary>
    /// Program numbers: the General MIDI instrument names, the Roland MT-32's built-in timbre
    /// names (its 128 preset patches, groups A and B, from the MT-32 owner's manual's tone list),
    /// and a General MIDI → MT-32 map. The map is the editor's own: for each GM instrument the
    /// MT-32 preset whose name and family come closest (pianos to pianos, string sections to
    /// string sections, synth pads to the MT-32's named pads...); some GM sounds have no MT-32
    /// counterpart (sound effects, some ethnic instruments) and get a near one.
    /// Both games' MT-32 themes use bank 0, that is these built-in presets: the MT-32 driver
    /// points patch p at built-in timbre p (group A for 0–63, B for 64–127) unless a bank above
    /// 0 is selected (exhume/runtime/port/sound/mt32.c setup_patch).
    /// </summary>
    public static class Mt32Programs
    {
        /// <summary>The 128 General MIDI instrument names, program 0 first.</summary>
        public static readonly string[] GmNames =
        {
            "Acoustic Grand Piano", "Bright Acoustic Piano", "Electric Grand Piano", "Honky-tonk Piano", "Electric Piano 1", "Electric Piano 2", "Harpsichord", "Clavinet",
            "Celesta", "Glockenspiel", "Music Box", "Vibraphone", "Marimba", "Xylophone", "Tubular Bells", "Dulcimer",
            "Drawbar Organ", "Percussive Organ", "Rock Organ", "Church Organ", "Reed Organ", "Accordion", "Harmonica", "Tango Accordion",
            "Nylon Guitar", "Steel Guitar", "Jazz Guitar", "Clean Guitar", "Muted Guitar", "Overdriven Guitar", "Distortion Guitar", "Guitar Harmonics",
            "Acoustic Bass", "Finger Bass", "Pick Bass", "Fretless Bass", "Slap Bass 1", "Slap Bass 2", "Synth Bass 1", "Synth Bass 2",
            "Violin", "Viola", "Cello", "Contrabass", "Tremolo Strings", "Pizzicato Strings", "Orchestral Harp", "Timpani",
            "String Ensemble 1", "String Ensemble 2", "Synth Strings 1", "Synth Strings 2", "Choir Aahs", "Voice Oohs", "Synth Voice", "Orchestra Hit",
            "Trumpet", "Trombone", "Tuba", "Muted Trumpet", "French Horn", "Brass Section", "Synth Brass 1", "Synth Brass 2",
            "Soprano Sax", "Alto Sax", "Tenor Sax", "Baritone Sax", "Oboe", "English Horn", "Bassoon", "Clarinet",
            "Piccolo", "Flute", "Recorder", "Pan Flute", "Blown Bottle", "Shakuhachi", "Whistle", "Ocarina",
            "Lead 1 (square)", "Lead 2 (sawtooth)", "Lead 3 (calliope)", "Lead 4 (chiff)", "Lead 5 (charang)", "Lead 6 (voice)", "Lead 7 (fifths)", "Lead 8 (bass + lead)",
            "Pad 1 (new age)", "Pad 2 (warm)", "Pad 3 (polysynth)", "Pad 4 (choir)", "Pad 5 (bowed)", "Pad 6 (metallic)", "Pad 7 (halo)", "Pad 8 (sweep)",
            "FX 1 (rain)", "FX 2 (soundtrack)", "FX 3 (crystal)", "FX 4 (atmosphere)", "FX 5 (brightness)", "FX 6 (goblins)", "FX 7 (echoes)", "FX 8 (sci-fi)",
            "Sitar", "Banjo", "Shamisen", "Koto", "Kalimba", "Bagpipe", "Fiddle", "Shanai",
            "Tinkle Bell", "Agogo", "Steel Drums", "Woodblock", "Taiko Drum", "Melodic Tom", "Synth Drum", "Reverse Cymbal",
            "Guitar Fret Noise", "Breath Noise", "Seashore", "Bird Tweet", "Telephone Ring", "Helicopter", "Applause", "Gunshot",
        };

        /// <summary>The MT-32's 128 built-in timbres (preset patches), program 0 first.</summary>
        public static readonly string[] Mt32Names =
        {
            "Acou Piano 1", "Acou Piano 2", "Acou Piano 3", "Elec Piano 1", "Elec Piano 2", "Elec Piano 3", "Elec Piano 4", "Honkytonk",
            "Elec Org 1", "Elec Org 2", "Elec Org 3", "Elec Org 4", "Pipe Org 1", "Pipe Org 2", "Pipe Org 3", "Accordion",
            "Harpsi 1", "Harpsi 2", "Harpsi 3", "Clavi 1", "Clavi 2", "Clavi 3", "Celesta 1", "Celesta 2",
            "Syn Brass 1", "Syn Brass 2", "Syn Brass 3", "Syn Brass 4", "Syn Bass 1", "Syn Bass 2", "Syn Bass 3", "Syn Bass 4",
            "Fantasy", "Harmo Pan", "Chorale", "Glasses", "Soundtrack", "Atmosphere", "Warm Bell", "Funny Vox",
            "Echo Bell", "Ice Rain", "Oboe 2001", "Echo Pan", "Doctor Solo", "School Daze", "Bellsinger", "Square Wave",
            "Str Sect 1", "Str Sect 2", "Str Sect 3", "Pizzicato", "Violin 1", "Violin 2", "Cello 1", "Cello 2",
            "Contrabass", "Harp 1", "Harp 2", "Guitar 1", "Guitar 2", "Elec Gtr 1", "Elec Gtr 2", "Sitar",
            "Acou Bass 1", "Acou Bass 2", "Elec Bass 1", "Elec Bass 2", "Slap Bass 1", "Slap Bass 2", "Fretless 1", "Fretless 2",
            "Flute 1", "Flute 2", "Piccolo 1", "Piccolo 2", "Recorder", "Pan Pipes", "Sax 1", "Sax 2",
            "Sax 3", "Sax 4", "Clarinet 1", "Clarinet 2", "Oboe", "Engl Horn", "Bassoon", "Harmonica",
            "Trumpet 1", "Trumpet 2", "Trombone 1", "Trombone 2", "Fr Horn 1", "Fr Horn 2", "Tuba", "Brs Sect 1",
            "Brs Sect 2", "Vibe 1", "Vibe 2", "Syn Mallet", "Windbell", "Glock", "Tube Bell", "Xylophone",
            "Marimba", "Koto", "Sho", "Shakuhachi", "Whistle 1", "Whistle 2", "Bottleblow", "Breathpipe",
            "Timpani", "Melodic Tom", "Deep Snare", "Elec Perc 1", "Elec Perc 2", "Taiko", "Taiko Rim", "Cymbal",
            "Castanets", "Triangle", "Orche Hit", "Telephone", "Bird Tweet", "One Note Jam", "Water Bell", "Jungle Tune",
        };

        /// <summary>General MIDI program → MT-32 built-in program (the editor's own map, see the class summary).</summary>
        public static readonly byte[] GmToMt32 =
        {
            // pianos, chromatic percussion
              0,   1,   2,   7,   3,   4,  16,  19,      22, 101, 101,  97, 104, 103, 102, 105,
            // organs, accordions; guitars
              8,   9,  10,  12,  14,  15,  87,  15,      59,  60,  61,  62,  61,  62,  62,  60,
            // basses; strings, harp, timpani
             64,  66,  67,  70,  68,  69,  28,  29,      52,  53,  54,  56,  49,  51,  57, 112,
            // ensembles, voices, hit; brass
             48,  50,  49,  50,  34,  34,  34, 122,      88,  90,  94,  89,  92,  95,  24,  25,
            // reeds; pipes
             78,  79,  80,  81,  84,  85,  86,  82,      74,  72,  76,  77, 110, 107, 108, 109,
            // synth leads; synth pads
             47,  44, 111,  73,  62,  39,  47,  30,      32,  36,  27,  34,  35,  38,  37,  33,
            // synth effects; ethnic
             41,  36, 100,  37,  40,  43,  43,  46,      63,  60, 105, 105,  99,  84,  52,  85,
            // percussive; sound effects
            126, 115,  99, 118, 117, 113, 116, 119,      59, 111,  41, 124, 123, 125, 119, 114,
        };

        public static string GmName(int program) { return program >= 0 && program < 128 ? GmNames[program] : "?"; }
        public static string Mt32Name(int program) { return program >= 0 && program < 128 ? Mt32Names[program] : "?"; }

        /// <summary>The MT-32 program for a General MIDI one.</summary>
        public static int Map(int gm) { return GmToMt32[Math.Max(0, Math.Min(127, gm))]; }
    }
}
