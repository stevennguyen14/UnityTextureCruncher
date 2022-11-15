using UnityEditor;
using UnityEngine;
using System.Collections;
using System.Linq;

public class TextureCruncher : EditorWindow
{
	#region Variables

	int compressionQuality = 75;
	int processingSpeed = 10;
	int _selected = 0;

	string[] _options = new string[3] { "512", "1024", "2048" };
	bool resourceFolderOnly = false;

	IEnumerator jobRoutine;
	IEnumerator messageRoutine;

	float progressCount = 0f;
	float totalCount = 1f;


	#endregion



	#region Properties

	float NormalizedProgress
	{
		get { return progressCount / totalCount; }
	}

	float Progress
	{
		get { return progressCount / totalCount * 100f; }
	}

	string FormattedProgress
	{
		get { return Progress.ToString("0.00") + "%"; }
	}

	#endregion


	#region Script Lifecylce

	[MenuItem("Window/Texture Cruncher")]
	static void Init()
	{
		var window = (TextureCruncher)EditorWindow.GetWindow(typeof(TextureCruncher));
		window.Show();
	}

	public void OnInspectorUpdate()
	{
		Repaint();
	}

	void OnGUI()
	{
		EditorGUILayout.LabelField("Texture Cruncher", EditorStyles.boldLabel);

		compressionQuality = EditorGUILayout.IntSlider("Compression quality:", compressionQuality, 0, 100);
		processingSpeed = EditorGUILayout.IntSlider("Processing speed:", processingSpeed, 1, 20);
		this._selected = EditorGUILayout.Popup("Max Size", _selected, _options);
		resourceFolderOnly = EditorGUILayout.Toggle("Resource folder only", resourceFolderOnly);


		string buttonLabel = jobRoutine != null ? "Cancel" : "Begin";
		if (GUILayout.Button(buttonLabel))
		{
			if (jobRoutine != null)
			{
				messageRoutine = DisplayMessage("Cancelled. " + FormattedProgress + " complete!", 4f);
				jobRoutine = null;
			}
			else
			{
				jobRoutine = CrunchTextures();
			}
		}

		if (jobRoutine != null)
		{
			EditorGUILayout.BeginHorizontal();

			EditorGUILayout.PrefixLabel(FormattedProgress);

			var rect = EditorGUILayout.GetControlRect();
			rect.width = rect.width * NormalizedProgress;
			GUI.Box(rect, GUIContent.none);

			EditorGUILayout.EndHorizontal();
		}
		else if (!string.IsNullOrEmpty(_message))
		{
			EditorGUILayout.HelpBox(_message, MessageType.None);
		}
	}

	void OnEnable()
	{
		EditorApplication.update += HandleCallbackFunction;
	}

	void HandleCallbackFunction()
	{
		if (jobRoutine != null && !jobRoutine.MoveNext())
			jobRoutine = null;


		if (messageRoutine != null && !messageRoutine.MoveNext())
			messageRoutine = null;
	}

	void OnDisable()
	{
		EditorApplication.update -= HandleCallbackFunction;
	}

	#endregion



	#region Logic

	string _message = null;

	IEnumerator DisplayMessage(string message, float duration = 0f)
	{
		if (duration <= 0f || string.IsNullOrEmpty(message))
			goto Exit;

		_message = message;

		while (duration > 0)
		{
			duration -= 0.01667f;
			yield return null;
		}

		Exit:
		_message = string.Empty;
	}
	//Find all textures that are not a multiple of 4 and convert them before doing the compression.
	IEnumerator CrunchTextures() 
	{
		DisplayMessage(string.Empty);

		if (!resourceFolderOnly)
		{
			//find textures in all folders
			var assets = AssetDatabase.FindAssets("t:texture", new[] { "Assets" }).Select(o => AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(o)) as TextureImporter);
			var eligibleAssets = assets.Where(o => o != null).Where(o => o.compressionQuality != compressionQuality || !o.crunchedCompression);

			totalCount = (float)eligibleAssets.Count();
			progressCount = 0f;

			int quality = compressionQuality;
			int limiter = processingSpeed;

			foreach (var textureImporter in eligibleAssets)
			{
				textureImporter.isReadable = true; //make it readable so texture width/height can be adjusted
				textureImporter.textureCompression = TextureImporterCompression.Uncompressed;   //If there is already crunch compression on the texture then it needs to be removed as texture can't be saved if compression is on.

				AssetDatabase.ImportAsset(textureImporter.assetPath);

				Texture2D tex = AssetDatabase.LoadAssetAtPath(textureImporter.assetPath, typeof(Texture2D)) as Texture2D;

				if (!IsDivisibleBy4(tex.width) || !IsDivisibleBy4(tex.height))
				{
					int width = tex.width;
					int height = tex.height;

					while (!IsDivisibleBy4(tex.width))
					{
						width++;
						TextureScale.Scale(tex, width, tex.height);
					}
					while (!IsDivisibleBy4(tex.height))
					{
						height++;
						TextureScale.Scale(tex, tex.width, height);
					}

					System.IO.File.WriteAllBytes(AssetDatabase.GetAssetPath(tex), tex.EncodeToPNG());
					AssetDatabase.Refresh();
				}

				textureImporter.textureCompression = TextureImporterCompression.Compressed; //Turn compression back on after resizing texture
				textureImporter.compressionQuality = quality;
				textureImporter.crunchedCompression = true;
				textureImporter.isReadable = false; //set it back to false so there's no internal duplicate texture

				//determine max texture size
				switch (_selected)
				{
				case 0:
					textureImporter.maxTextureSize = 512;
					break;
				case 1:
					textureImporter.maxTextureSize = 1024;
					break;
				case 2:
					textureImporter.maxTextureSize = 2048;
					break;
				}

				textureImporter.SaveAndReimport();

				progressCount += 1f;

				limiter -= 1;
				if (limiter <= 0)
				{
					yield return null;

					limiter = processingSpeed;
				}
			}

		}
		else
		{
			//find textures in resources folder only
			var resourseAssets = AssetDatabase.FindAssets("t:texture", new[] { "Assets/Resources" }).Select(o => AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(o)) as TextureImporter);
			var eligibleAssets = resourseAssets.Where(o => o != null).Where(o => o.compressionQuality != compressionQuality || !o.crunchedCompression);

			totalCount = (float)eligibleAssets.Count();
			progressCount = 0f;

			int quality = compressionQuality;
			int limiter = processingSpeed;

			foreach (var textureImporter in eligibleAssets)
			{
				textureImporter.isReadable = true; //make it readable so texture width/height can be adjusted
				textureImporter.textureCompression = TextureImporterCompression.Uncompressed;   //If there is already crunch compression on the texture then it needs to be removed as texture can't be saved if compression is on.

				AssetDatabase.ImportAsset(textureImporter.assetPath);

				Texture2D tex = AssetDatabase.LoadAssetAtPath(textureImporter.assetPath, typeof(Texture2D)) as Texture2D;

				if(!IsDivisibleBy4(tex.width) || !IsDivisibleBy4(tex.height))
				{
					int width = tex.width;
					int height = tex.height;

					while (!IsDivisibleBy4(tex.width))
					{
						width++;
						TextureScale.Scale(tex, width, tex.height);
					}
					while (!IsDivisibleBy4(tex.height))
					{
						height++;
						TextureScale.Scale(tex, tex.width, height);
					}

					System.IO.File.WriteAllBytes(AssetDatabase.GetAssetPath(tex), tex.EncodeToPNG());
					AssetDatabase.Refresh();
				}

				textureImporter.textureCompression = TextureImporterCompression.Compressed; //Turn compression back on after resizing texture
				textureImporter.compressionQuality = quality;
				textureImporter.crunchedCompression = true;
				textureImporter.isReadable = false; //set it back to false so there's no internal duplicate texture

				//determine max texture size
				switch (_selected)
				{
				case 0:
					textureImporter.maxTextureSize = 512;
					break;
				case 1:
					textureImporter.maxTextureSize = 1024;
					break;
				case 2:
					textureImporter.maxTextureSize = 2048;
					break;
				}

				textureImporter.SaveAndReimport();

				progressCount += 1f;

				limiter -= 1;
				if (limiter <= 0)
				{
					yield return null;

					limiter = processingSpeed;
				}
			}

		}

		messageRoutine = DisplayMessage("Crunching complete!", 6f);
		jobRoutine = null;
	}

	private bool IsDivisibleBy4(int num)
	{
		return (num % 4) == 0;
	}

	public class TextureScale
	{
		private static Color[] texColors;
		private static Color[] newColors;
		private static int w;
		private static float ratioX;
		private static float ratioY;
		private static int w2;

		public static void Scale(Texture2D tex, int newWidth, int newHeight)
		{
			texColors = tex.GetPixels();
			newColors = new Color[newWidth * newHeight];
			ratioX = 1.0f / ((float)newWidth / (tex.width - 1));
			ratioY = 1.0f / ((float)newHeight / (tex.height - 1));
			w = tex.width;
			w2 = newWidth;

			BilinearScale(0, newHeight);

			tex.Resize(newWidth, newHeight);
			tex.SetPixels(newColors);
			tex.Apply();
		}

		private static void BilinearScale(int start, int end)
		{
			for (var y = start; y < end; y++)
			{
				int yFloor = (int)Mathf.Floor(y * ratioY);
				var y1 = yFloor * w;
				var y2 = (yFloor + 1) * w;
				var yw = y * w2;

				for (var x = 0; x < w2; x++)
				{
					int xFloor = (int)Mathf.Floor(x * ratioX);
					var xLerp = x * ratioX - xFloor;
					newColors[yw + x] = ColorLerpUnclamped(ColorLerpUnclamped(texColors[y1 + xFloor], texColors[y1 + xFloor + 1], xLerp),
														   ColorLerpUnclamped(texColors[y2 + xFloor], texColors[y2 + xFloor + 1], xLerp),
														   y * ratioY - yFloor);
				}
			}
		}

		private static Color ColorLerpUnclamped(Color c1, Color c2, float value)
		{
			return new Color(c1.r + (c2.r - c1.r) * value,
							  c1.g + (c2.g - c1.g) * value,
							  c1.b + (c2.b - c1.b) * value,
							  c1.a + (c2.a - c1.a) * value);
		}
	}

	#endregion

}