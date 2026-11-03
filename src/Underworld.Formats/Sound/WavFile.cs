using System;
using System.IO;
using System.Text;

namespace UltimateEditor.Sound
{
	/// <summary>
	/// A RIFF WAVE file read into floating-point samples (−1 to 1, one array per channel), and
	/// written as 8-bit unsigned or 16-bit signed PCM. Reads PCM of 8, 16, 24 and 32 bits,
	/// 32- and 64-bit float, WAVE_FORMAT_EXTENSIBLE with either subformat, any rate and any
	/// number of channels. Chunks other than fmt and data are skipped.
	/// </summary>
	public sealed class WavFile
	{
		public int SampleRate { get; private set; }
		/// <summary>The samples of each channel, −1 to 1.</summary>
		public float[][] Channels { get; private set; }
		/// <summary>The bits per sample of the file read (8 for one made from 8-bit samples).</summary>
		public int Bits { get; private set; }
		/// <summary>The file held floating-point samples.</summary>
		public bool IsFloat { get; private set; }

		public int ChannelCount { get { return Channels.Length; } }
		public int Length { get { return Channels.Length == 0 ? 0 : Channels[0].Length; } }
		public double Seconds { get { return SampleRate <= 0 ? 0 : (double)Length / SampleRate; } }

		public WavFile(int rate, float[][] channels, int bits = 16)
		{
			if (channels == null || channels.Length == 0) throw new ArgumentException("A WAV file needs at least one channel.");
			SampleRate = rate; Channels = channels; Bits = bits;
		}

		/// <summary>A mono file from 8-bit unsigned samples (a VOC's), kept exactly: 0x80 is 0.</summary>
		public static WavFile FromUnsigned8(byte[] samples, int rate)
		{
			var c = new float[samples.Length];
			for (int i = 0; i < c.Length; i++) c[i] = (samples[i] - 128) / 128f;
			return new WavFile(rate, new[] { c }, 8);
		}

		public static WavFile Load(string path) { return Parse(File.ReadAllBytes(path)); }

		/// <summary>Reads a WAV file; throws InvalidDataException with the reason when it cannot.</summary>
		public static WavFile Parse(byte[] d)
		{
			if (d == null || d.Length < 12 || Tag(d, 0) != "RIFF" || Tag(d, 8) != "WAVE") throw new InvalidDataException("Not a WAV file (no RIFF WAVE header).");
			int format = -1, channels = 0, rate = 0, bits = 0, align = 0;
			int dataStart = -1, dataLen = 0;
			int p = 12;
			while (p + 8 <= d.Length)
			{
				string id = Tag(d, p);
				long len = (uint)(d[p + 4] | d[p + 5] << 8 | d[p + 6] << 16 | d[p + 7] << 24);
				int body = p + 8;
				if (id == "fmt " && len >= 16 && body + 16 <= d.Length)
				{
					format = U16(d, body); channels = U16(d, body + 2); rate = (int)U32(d, body + 4);
					align = U16(d, body + 12); bits = U16(d, body + 14);
					if (format == 0xFFFE && len >= 40 && body + 40 <= d.Length) format = U16(d, body + 24);   // the subformat GUID's first two bytes
				}
				else if (id == "data")
				{
					dataStart = body;
					dataLen = (int)Math.Min(len, d.Length - body);   // a writer that left the length 0 or too big: what is there
					if (len == 0 || len == 0xFFFFFFFF) dataLen = d.Length - body;
				}
				long next = body + len + (len & 1);
				if (next <= p || next > int.MaxValue) break;
				p = (int)next;
			}
			if (format < 0) throw new InvalidDataException("The WAV file has no fmt chunk.");
			if (dataStart < 0) throw new InvalidDataException("The WAV file has no data chunk.");
			if (channels < 1) throw new InvalidDataException("The WAV file has no channels.");
			if (rate < 1) throw new InvalidDataException("The WAV file has no sample rate.");
			bool isFloat = format == 3;
			if (format != 1 && format != 3) throw new InvalidDataException("The WAV file is compressed (format " + format + "): save it as PCM or float.");
			if (isFloat ? bits != 32 && bits != 64 : bits != 8 && bits != 16 && bits != 24 && bits != 32)
				throw new InvalidDataException("The WAV file has " + bits + "-bit " + (isFloat ? "float" : "PCM") + " samples: 8, 16, 24 or 32-bit PCM, or 32-bit float, are read.");
			int bytes = bits / 8;
			int frame = Math.Max(align, bytes * channels);
			int n = dataLen / frame;
			var ch = new float[channels][];
			for (int c = 0; c < channels; c++) ch[c] = new float[n];
			for (int i = 0; i < n; i++)
			{
				int f = dataStart + i * frame;
				for (int c = 0; c < channels; c++)
				{
					int o = f + c * bytes;
					float v;
					if (isFloat) v = bits == 32 ? BitConverter.ToSingle(d, o) : (float)BitConverter.ToDouble(d, o);
					else switch (bits)
						{
							case 8: v = (d[o] - 128) / 128f; break;
							case 16: v = (short)(d[o] | d[o + 1] << 8) / 32768f; break;
							case 24: v = ((d[o] << 8 | d[o + 1] << 16 | d[o + 2] << 24) >> 8) / 8388608f; break;
							default: v = (float)((d[o] | d[o + 1] << 8 | d[o + 2] << 16 | d[o + 3] << 24) / 2147483648.0); break;
						}
					if (float.IsNaN(v) || float.IsInfinity(v)) v = 0;
					ch[c][i] = v;
				}
			}
			return new WavFile(rate, ch, bits) { IsFloat = isFloat };
		}

		/// <summary>The channels mixed to one (their mean).</summary>
		public float[] Mono()
		{
			if (Channels.Length == 1) return (float[])Channels[0].Clone();
			var m = new float[Length];
			for (int i = 0; i < m.Length; i++)
			{
				double s = 0;
				for (int c = 0; c < Channels.Length; c++) s += Channels[c][i];
				m[i] = (float)(s / Channels.Length);
			}
			return m;
		}

		/// <summary>
		/// The file as 8-bit unsigned (bits 8) or 16-bit signed PCM, every channel, with a plain
		/// 16-byte fmt chunk. 8-bit samples made by FromUnsigned8 come back exactly.
		/// </summary>
		public byte[] ToBytes(int bits = 16)
		{
			if (bits != 8 && bits != 16) throw new ArgumentException("WAV files are written with 8 or 16 bits.");
			int ch = Channels.Length, bytes = bits / 8, n = Length;
			int dataLen = n * ch * bytes;
			var ms = new MemoryStream(44 + dataLen + 1);
			var w = new BinaryWriter(ms);
			w.Write(Encoding.ASCII.GetBytes("RIFF"));
			w.Write(36 + dataLen + (dataLen & 1));
			w.Write(Encoding.ASCII.GetBytes("WAVE"));
			w.Write(Encoding.ASCII.GetBytes("fmt "));
			w.Write(16);
			w.Write((short)1);
			w.Write((short)ch);
			w.Write(SampleRate);
			w.Write(SampleRate * ch * bytes);
			w.Write((short)(ch * bytes));
			w.Write((short)bits);
			w.Write(Encoding.ASCII.GetBytes("data"));
			w.Write(dataLen);
			for (int i = 0; i < n; i++)
				for (int c = 0; c < ch; c++)
				{
					float v = Channels[c][i];
					if (bits == 8) w.Write((byte)Math.Max(0, Math.Min(255, (int)Math.Round(v * 128) + 128)));
					else w.Write((short)Math.Max(-32768, Math.Min(32767, (int)Math.Round(v * 32768))));
				}
			if ((dataLen & 1) != 0) w.Write((byte)0);
			w.Flush();
			return ms.ToArray();
		}

		static string Tag(byte[] d, int p) { return p + 4 <= d.Length ? Encoding.ASCII.GetString(d, p, 4) : ""; }
		static int U16(byte[] d, int p) { return d[p] | d[p + 1] << 8; }
		static uint U32(byte[] d, int p) { return (uint)(d[p] | d[p + 1] << 8 | d[p + 2] << 16 | d[p + 3] << 24); }
	}
}
