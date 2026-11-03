using System;
using System.Collections.Generic;
using System.IO;

namespace UltimateEditor.Sound
{
	/// <summary>One block of a Creative Voice file: its type and the bytes after its 4-byte header.</summary>
	public sealed class VocBlock
	{
		/// <summary>1 sound data, 2 continuation, 3 silence, 4 marker, 5 text, 6 repeat, 7 end repeat, 8 extended, 9 new sound data.</summary>
		public int Type;
		public byte[] Data;
	}

	/// <summary>
	/// A Creative Voice file (.VOC): the 26-byte header ("Creative Voice File", 0x1A, the data
	/// offset, the version and its check word) and its blocks, each a type byte and a 24-bit
	/// length, ended by a type-0 terminator. Read keeps every byte (ToBytes gives the file back
	/// unchanged). The games' players do not parse the blocks: AIL reads the first block's rate
	/// (AIL_index_VOC_block), then the game skips 0x20 bytes and streams the rest of the file as
	/// samples (uw1/src/sound/SOUND.C play_speech, uw2/src/sound/SOUND.C load_dfx_page and
	/// big_speech_play). So a file the game plays well is the shape FromSamples writes: version
	/// 1.10, data at 0x1A, one type-1 block of 8-bit unsigned mono samples (codec 0), the
	/// terminator.
	/// </summary>
	public sealed class VocFile
	{
		public const string Signature = "Creative Voice File";
		/// <summary>The header, the block header and the rate and codec bytes the game skips before the samples.</summary>
		public const int GameSkip = 0x20;
		/// <summary>The bytes a game-shaped file adds to its samples: the 32 skipped and the terminator.</summary>
		public const int Overhead = GameSkip + 1;

		/// <summary>The bytes before the first block (the header, normally 26).</summary>
		public byte[] Header { get; private set; }
		public readonly List<VocBlock> Blocks = new List<VocBlock>();
		/// <summary>The file ends with a type-0 terminator.</summary>
		public bool Terminated { get; private set; }
		/// <summary>Bytes after the terminator (or a block cut short), kept for the round trip.</summary>
		public byte[] Trailing { get; private set; } = new byte[0];

		public int DataOffset { get { return Header.Length >= 22 ? Header[20] | Header[21] << 8 : Header.Length; } }
		public int Version { get { return Header.Length >= 24 ? Header[22] | Header[23] << 8 : 0; } }

		public static VocFile Load(string path) { return Parse(File.ReadAllBytes(path)); }

		/// <summary>Reads a .VOC; throws InvalidDataException when the signature is missing.</summary>
		public static VocFile Parse(byte[] d)
		{
			if (d == null || d.Length < 26) throw new InvalidDataException("Too short for a .VOC file.");
			for (int i = 0; i < Signature.Length; i++)
				if (d[i] != Signature[i]) throw new InvalidDataException("Not a Creative Voice file (no \"Creative Voice File\" signature).");
			int off = d[20] | d[21] << 8;
			if (off < 26 || off > d.Length) throw new InvalidDataException("The .VOC data offset is out of the file.");
			var v = new VocFile { Header = Sub(d, 0, off) };
			int p = off;
			while (p < d.Length)
			{
				int t = d[p];
				if (t == 0) { v.Terminated = true; p++; break; }
				if (p + 4 > d.Length) break;
				int len = d[p + 1] | d[p + 2] << 8 | d[p + 3] << 16;
				if (p + 4 + len > d.Length) break;
				v.Blocks.Add(new VocBlock { Type = t, Data = Sub(d, p + 4, len) });
				p += 4 + len;
			}
			v.Trailing = Sub(d, p, d.Length - p);
			return v;
		}

		/// <summary>The file as read (or as built), byte for byte.</summary>
		public byte[] ToBytes()
		{
			var ms = new MemoryStream();
			ms.Write(Header, 0, Header.Length);
			foreach (var b in Blocks)
			{
				ms.WriteByte((byte)b.Type);
				ms.WriteByte((byte)b.Data.Length); ms.WriteByte((byte)(b.Data.Length >> 8)); ms.WriteByte((byte)(b.Data.Length >> 16));
				ms.Write(b.Data, 0, b.Data.Length);
			}
			if (Terminated) ms.WriteByte(0);
			ms.Write(Trailing, 0, Trailing.Length);
			return ms.ToArray();
		}

		/// <summary>
		/// The file the games play cleanly: VOC 1.10 header (data offset 0x1A, check word
		/// 0x1129), one type-1 block (time constant, codec 0, the samples), the terminator.
		/// samples: 8-bit unsigned mono.
		/// </summary>
		public static VocFile FromSamples(byte[] samples, int timeConstant)
		{
			if (timeConstant < 0 || timeConstant > 255) throw new ArgumentOutOfRangeException(nameof(timeConstant));
			if (samples.Length + 2 > 0xFFFFFF) throw new ArgumentException("Too many samples for one .VOC block.");
			var h = new byte[26];
			for (int i = 0; i < Signature.Length; i++) h[i] = (byte)Signature[i];
			h[19] = 0x1A;
			h[20] = 0x1A; h[21] = 0;
			int ver = 0x010A, check = (~ver + 0x1234) & 0xFFFF;
			h[22] = (byte)ver; h[23] = (byte)(ver >> 8);
			h[24] = (byte)check; h[25] = (byte)(check >> 8);
			var data = new byte[samples.Length + 2];
			data[0] = (byte)timeConstant;
			data[1] = 0;
			Array.Copy(samples, 0, data, 2, samples.Length);
			var v = new VocFile { Header = h, Terminated = true };
			v.Blocks.Add(new VocBlock { Type = 1, Data = data });
			return v;
		}

		/// <summary>Why the file is not the shape the game needs, or null when it is.</summary>
		public string ShapeProblem
		{
			get
			{
				if (Header.Length != 0x1A) return "its data does not start at 0x1A";
				if (Blocks.Count == 0) return "it has no sound block";
				if (Blocks[0].Type != 1) return "its first block is of type " + Blocks[0].Type + ", not 1 (the game would play the block headers as clicks)";
				if (Blocks.Count > 1) return "it has " + Blocks.Count + " blocks (the game plays the later block headers as clicks)";
				if (Blocks[0].Data.Length < 2 || Blocks[0].Data[1] != 0) return "its samples are packed (codec " + (Blocks[0].Data.Length < 2 ? -1 : Blocks[0].Data[1]) + "), not plain 8-bit";
				if (!Terminated) return "it has no terminator";
				if (Trailing.Length > 0) return "it has bytes after the terminator";
				return null;
			}
		}

		/// <summary>The time constant the rate comes from (the first type-1 block's), or -1.</summary>
		public int TimeConstant
		{
			get { foreach (var b in Blocks) if (b.Type == 1 && b.Data.Length >= 1) return b.Data[0]; return -1; }
		}

		/// <summary>
		/// The sample rate a player uses: an extended block (8) before the sound data sets it
		/// (256,000,000 / (65536 − its time constant), halved for stereo), else the type-1 time
		/// constant (1,000,000 / (256 − TC)), or a type-9 block's own rate. 0 when there is none.
		/// </summary>
		public int SampleRate
		{
			get
			{
				foreach (var b in Blocks)
				{
					if (b.Type == 8 && b.Data.Length >= 4)
					{
						int tc = b.Data[0] | b.Data[1] << 8;
						int ch = b.Data[3] + 1;
						return (int)Math.Round(256000000.0 / (65536 - tc) / ch);
					}
					if (b.Type == 1 && b.Data.Length >= 1) return RateOf(b.Data[0]);
					if (b.Type == 9 && b.Data.Length >= 4) return b.Data[0] | b.Data[1] << 8 | b.Data[2] << 16 | b.Data[3] << 24;
				}
				return 0;
			}
		}

		/// <summary>The rate of a time constant: 1,000,000 / (256 − TC), rounded.</summary>
		public static int RateOf(int timeConstant) { return (int)Math.Round(RateOfExact(timeConstant)); }

		public static double RateOfExact(int timeConstant) { return 1000000.0 / (256 - timeConstant); }

		/// <summary>The time constant nearest a rate, never giving more than maxRate.</summary>
		public static int TimeConstantFor(double rate, double maxRate = 22050)
		{
			int div = (int)Math.Round(1000000.0 / Math.Max(1, rate));
			if (div < 1) div = 1;
			while (1000000.0 / div > maxRate + 0.5) div++;
			if (div > 256) div = 256;
			return 256 - div;
		}

		/// <summary>
		/// The sound as 8-bit unsigned mono samples: the data of type-1 (codec 0) and type-2
		/// blocks, type-3 silences as 0x80, type-9 8-bit or 16-bit PCM (stereo mixed down).
		/// Other blocks and packed data are left out.
		/// </summary>
		public byte[] Samples8()
		{
			var ms = new MemoryStream();
			bool plain = true;
			int channels = 1;
			foreach (var b in Blocks)
			{
				switch (b.Type)
				{
					case 1:
						plain = b.Data.Length >= 2 && b.Data[1] == 0;
						if (plain) ms.Write(b.Data, 2, b.Data.Length - 2);
						break;
					case 2:
						if (plain) ms.Write(b.Data, 0, b.Data.Length);
						break;
					case 3:
						if (b.Data.Length >= 2) { int n = (b.Data[0] | b.Data[1] << 8) + 1; for (int i = 0; i < n; i++) ms.WriteByte(0x80); }
						break;
					case 8:
						if (b.Data.Length >= 4) channels = b.Data[3] + 1;
						break;
					case 9:
						if (b.Data.Length >= 12)
						{
							int bits = b.Data[4], ch = Math.Max(1, (int)b.Data[5]), codec = b.Data[6] | b.Data[7] << 8;
							for (int i = 12; i + (bits / 8) * ch <= b.Data.Length; i += (bits / 8) * ch)
							{
								int sum = 0;
								for (int c = 0; c < ch; c++)
								{
									if (bits == 8 && codec == 0) sum += b.Data[i + c];
									else if (bits == 16 && codec == 4) sum += ((short)(b.Data[i + c * 2] | b.Data[i + c * 2 + 1] << 8) >> 8) + 128;
									else sum += 128;
								}
								ms.WriteByte((byte)(sum / ch));
							}
						}
						break;
				}
			}
			var s = ms.ToArray();
			if (channels > 1)
			{
				var m = new byte[s.Length / channels];
				for (int i = 0; i < m.Length; i++) { int sum = 0; for (int c = 0; c < channels; c++) sum += s[i * channels + c]; m[i] = (byte)(sum / channels); }
				return m;
			}
			return s;
		}

		/// <summary>
		/// The bytes the game plays as sound: everything after the first 0x20 bytes of the file,
		/// as SOUND.C streams it (block headers and the terminator included), cut to limit file
		/// bytes when the game reads no more than that.
		/// </summary>
		public static int PlayedBytes(int fileLength, int limit = int.MaxValue)
		{
			return Math.Max(0, Math.Min(fileLength, limit) - GameSkip);
		}

		static byte[] Sub(byte[] d, int start, int len)
		{
			var r = new byte[Math.Max(0, len)];
			if (len > 0) Array.Copy(d, start, r, 0, len);
			return r;
		}
	}
}
