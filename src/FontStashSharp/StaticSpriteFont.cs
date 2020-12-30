﻿using Cyotek.Drawing.BitmapFont;
using FontStashSharp.Interfaces;
using StbImageSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

#if MONOGAME || FNA
using Microsoft.Xna.Framework;
#elif STRIDE
using Stride.Core.Mathematics;
#else
using System.Drawing;
#endif

namespace FontStashSharp
{
	public class TextureWithOffset
	{
		public ITexture2D Texture { get; set; }
		public Point Offset { get; set; }

		public TextureWithOffset(ITexture2D texture)
		{
			if (texture == null)
			{
				throw new ArgumentNullException("texture");
			}

			Texture = texture;
		}

		public TextureWithOffset(ITexture2D texture, Point offset) : this(texture)
		{
			Offset = offset;
		}
	}

	public partial class StaticSpriteFont : SpriteFontBase
	{
		public Int32Map<FontGlyph> Glyphs { get; } = new Int32Map<FontGlyph>();

		public int? DefaultCharacter { get; set; }

		public int LineHeight { get; private set; }

		public StaticSpriteFont(int lineHeight)
		{
			LineHeight = lineHeight;
		}

		private FontGlyph InternalGetGlyph(int codepoint)
		{
			FontGlyph result;
			Glyphs.TryGetValue(codepoint, out result);

			return result;
		}

		protected override FontGlyph GetGlyph(int codepoint, bool withoutBitmap)
		{
			var result = InternalGetGlyph(codepoint);
			if (result == null && DefaultCharacter != null)
			{
				result = InternalGetGlyph(DefaultCharacter.Value);
			}
			return result;
		}

		protected override void PreDraw(string str, out float ascent, out float lineHeight)
		{
			ascent = 0;
			lineHeight = LineHeight;
		}

		protected override void PreDraw(StringBuilder str, out float ascent, out float lineHeight)
		{
			ascent = 0;
			lineHeight = LineHeight;
		}

		private static BitmapFont LoadBMFont(string data)
		{
			var bmFont = new BitmapFont();
			if (data.StartsWith("<"))
			{
				// xml
				bmFont.LoadXml(data);
			}
			else
			{
				bmFont.LoadText(data);
			}

			return bmFont;
		}

		private static StaticSpriteFont FromBMFont(BitmapFont bmFont, Func<string, TextureWithOffset> textureGetter)
		{
			var result = new StaticSpriteFont(bmFont.LineHeight);

			var characters = bmFont.Characters.Values.OrderBy(c => c.Char);

			foreach (var ch in characters)
			{
				var texture = textureGetter(bmFont.Pages[ch.TexturePage].FileName);
				var bounds = new Rectangle(ch.X, ch.Y, ch.Width, ch.Height);
				bounds.Offset(texture.Offset);

				var glyph = new FontGlyph
				{
					Id = ch.Char,
					Codepoint = ch.Char,
					Bounds = bounds,
					XOffset = ch.XOffset,
					YOffset = ch.YOffset,
					XAdvance = ch.XAdvance,
					Texture = texture.Texture
				};

				result.Glyphs[glyph.Codepoint] = glyph;
			}

			return result;
		}

		private static int PowerOfTwo(int x)
		{
			int power = 1;
			while (power < x)
				power *= 2;

			return power;
		}

		public static StaticSpriteFont FromBMFont(string data, Func<string, TextureWithOffset> textureGetter)
		{
			var bmFont = LoadBMFont(data);
			return FromBMFont(bmFont, textureGetter);
		}

		public unsafe static StaticSpriteFont FromBMFont(string data, Func<string, Stream> imageStreamOpener, ITexture2DCreator texture2DCreator)
		{
			var bmFont = LoadBMFont(data);

			var textures = new Dictionary<string, ITexture2D>();
			for (var i = 0; i < bmFont.Pages.Length; ++i)
			{
				var fileName = bmFont.Pages[i].FileName;
				Stream stream = null;
				try
				{
					stream = imageStreamOpener(fileName);
					if (!stream.CanSeek)
					{
						// If stream isn't seekable, use MemoryStream instead
						var ms = new MemoryStream();
						stream.CopyTo(ms);
						ms.Seek(0, SeekOrigin.Begin);
						stream.Dispose();
						stream = ms;
					}

					var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
					if (image.SourceComp == ColorComponents.Grey)
					{
						// If input image is single byte per pixel, then StbImageSharp will set alpha to 255 in the resulting 32-bit image
						// Such behavior isn't acceptable for us
						// So reset alpha to color value
						for (var j = 0; j < image.Data.Length / 4; ++j)
						{
							image.Data[j * 4 + 3] = image.Data[j * 4];
						}
					}

					var width = PowerOfTwo(image.Width);
					var height = PowerOfTwo(image.Height);

					var texture = texture2DCreator.Create(width, height);

					texture.SetData(new Rectangle(0, 0, image.Width, image.Height), image.Data);

					textures[fileName] = texture;
				}
				finally
				{
					stream.Dispose();
				}
			}

			return FromBMFont(bmFont, fileName => new TextureWithOffset(textures[fileName]));
		}
	}
}