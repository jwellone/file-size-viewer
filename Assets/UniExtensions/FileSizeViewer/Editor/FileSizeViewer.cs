using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEngine.Profiling;
using UObject = UnityEngine.Object;

namespace UniExtensions.Editor
{
	public class FileSizeViewer : EditorWindow
	{
		private static MethodInfo s_getStorageMemorySize;

		public enum eSort
		{
			None = -1,
			Name,
			Size,
			Num,
		}


		class Mask
		{
			public int mask = -1;
			public string[] categorys = new string[1];

			public Mask()
			{
			}

			public Mask(int index, string[] target = null)
			{
				var start = Math.Min(index * 32, target.Length);
				var end = Math.Min(start + 32, target.Length);
				var count = end - start;
				categorys = new string[count];
				index *= 32;
				for (var i = 0; i < count; ++i)
				{
					categorys[i] = target[i + index];
				}
			}
		}

		class SizeInfo
		{
			public long size { get; protected set; }
			public float sizeKB { get { return (size / 1024.0f); } }
			public float sizeMB { get { return (sizeKB / 1024.0f); } }
			public string formatByte { get { return EditorUtility.FormatBytes(size); } }
		}

		class Asset : SizeInfo
		{
			public string path { get; private set; }
			public string name { get; private set; }
			public float rate { get; private set; }
			public Texture texture { get; private set; }

			public Asset(string _path)
			{
				path = _path;
				name = System.IO.Path.GetFileNameWithoutExtension(path);
				size = FileSizeViewer.GetAssetsSize(path);

				var tex = AssetDatabase.LoadAssetAtPath(path, typeof(Texture)) as Texture;
				if (tex != null)
				{
					name += " " + tex.width + "x" + tex.height + " " + tex.graphicsFormat;
				}
			}

			public void Setup(long max)
			{
				rate = (float)((double)size / (double)max);
				texture = AssetDatabase.GetCachedIcon(path);
			}
		}

		class AssetList : SizeInfo
		{
			public eSort sorted { get; private set; }
			public long minSize { get; private set; }
			public long maxSize { get; private set; }
			public long avgSize { get { return (0 >= assets.Count) ? 0 : (int)((float)maxSize / assets.Count); } }
			public string formatMinSize { get { return EditorUtility.FormatBytes(minSize); } }
			public string formatMaxSize { get { return EditorUtility.FormatBytes(maxSize); } }
			public string formatAvgSize { get { return EditorUtility.FormatBytes(avgSize); } }
			private string[] names = null;
			private bool[] isSortByAscendingOrders = new bool[(int)eSort.Num];
			public List<Asset> assets { get; private set; }

			public AssetList()
			{
				assets = new List<Asset>();
				Clear();
			}

			public void Add(Asset asset)
			{
				var _size = asset.size;

				size += _size;
				sorted = eSort.None;

				UpdateSizeMinMax(_size);

				assets.Add(asset);
			}

			public void Remove(Asset asset)
			{
				var _size = asset.size;

				size -= _size;
				sorted = eSort.None;

				if (maxSize == _size || minSize == _size)
				{
					minSize = int.MaxValue;
					maxSize = 0;
					for (int i = 0; i < assets.Count; ++i)
					{
						UpdateSizeMinMax(assets[i].size);
					}
				}

				assets.Remove(asset);
			}

			public void Clear()
			{
				size = 0;
				minSize = int.MaxValue;
				maxSize = 0;
				sorted = eSort.None;
				names = null;
				assets.Clear();

				for (int i = 0; i < (int)eSort.Num; ++i)
				{
					isSortByAscendingOrders[i] = false;
				}
			}

			public void Sort(eSort _sort, bool isAscendingOrder)
			{
				if (eSort.None == _sort ||
					 sorted == _sort && isAscendingOrder == IsSortByAscendingOrder(_sort))
				{
					return;
				}

				names = null;

				SetSortState(_sort, isAscendingOrder);

				sorted = _sort;

				switch (_sort)
				{
					case eSort.Name:
						{
							if (isAscendingOrder) { assets.Sort(delegate (Asset a, Asset b) { return string.Compare(a.name, b.name); }); }
							else { assets.Sort(delegate (Asset a, Asset b) { return string.Compare(b.name, a.name); }); }
						}
						break;

					case eSort.Size:
					default:
						{
							if (isAscendingOrder) { assets.Sort(delegate (Asset a, Asset b) { return (int)(a.size - b.size); }); }
							else { assets.Sort(delegate (Asset a, Asset b) { return (int)(b.size - a.size); }); }
						}
						break;
				}
			}

			public bool IsSortByAscendingOrder(eSort _sort)
			{
				return _sort == eSort.None ? false : isSortByAscendingOrders[(int)_sort];
			}

			private void SetSortState(eSort _sort, bool isSortByAscendingOrder)
			{
				if (_sort != eSort.None)
				{
					isSortByAscendingOrders[(int)_sort] = isSortByAscendingOrder;
				}
			}

			public void Setup()
			{
				var max = (0 >= maxSize) ? 1 : maxSize;
				for (int i = 0; i < assets.Count; ++i)
				{
					assets[i].Setup(max);
				}

				names = null;
			}

			public string[] GetNames()
			{
				if (names == null)
				{
					names = new string[assets.Count];
					for (var i = 0; i < assets.Count; ++i)
					{
						names[i] = assets[i].name;
					}
				}
				return names;
			}

			private void UpdateSizeMinMax(long _size)
			{
				if (maxSize < _size)
				{
					maxSize = _size;
				}

				if (minSize > _size)
				{
					minSize = _size;
				}
			}
		}

		private bool m_isUpdateSelectInfo = false;
		private int m_nSelected = -1;
		private float m_delta = 0.0f;
		private Vector2 m_vScrollPos = Vector2.zero;
		private AssetList m_selectInfo = new AssetList();
		private Mask[] m_masks = new Mask[] { new Mask() };
		private readonly Dictionary<string, AssetList> m_dicInfo = new Dictionary<string, AssetList>();

		[MenuItem("UniExtensions/Window/File Size Viewer")]
		private static void Open()
		{
			var window = EditorWindow.GetWindow<FileSizeViewer>("FileSizeViewer");
			window.position = new Rect(500, 300, 300, 500);
			window.minSize = new Vector2(400, 300);
			window.Show();
		}

		private void OnEnable()
		{
			Clear();
		}

		private void OnDisable()
		{
			Clear();
		}

		private void OnGUI()
		{
			OnGUIAssets();

			EditorGUILayout.BeginHorizontal();
			{
				const int CONST_OPERATION_WIDTH = 53;

				GUILayout.FlexibleSpace();

				for (var i = 0; i < m_masks.Length; ++i)
				{
					var selectMask = EditorGUILayout.MaskField(m_masks[i].mask, m_masks[i].categorys, GUILayout.Width(CONST_OPERATION_WIDTH << 1));
					ChangeCategory(i, selectMask);
				}

				GUI.enabled = (GetSelectAssetList().assets.Count >= 1);
				GUI.backgroundColor = (GUI.enabled) ? Color.yellow : Color.white;
				if (GUILayout.Button("保存", new GUILayoutOption[] { GUILayout.Width(CONST_OPERATION_WIDTH), GUILayout.Height(20) }))
				{
					Save();
				}

				GUI.enabled = true;
				GUI.backgroundColor = Color.yellow;
				if (GUILayout.Button("読込", new GUILayoutOption[] { GUILayout.Width(CONST_OPERATION_WIDTH), GUILayout.Height(20) }))
				{
					Read();
				}
				GUI.backgroundColor = Color.white;
			}
			EditorGUILayout.EndHorizontal();

			var list = GetSelectAssetList();
			string strInfo = list.assets.Count + "ファイル";
			strInfo += " 最小" + list.formatMinSize;
			strInfo += " 最大" + list.formatMaxSize;
			strInfo += " 合計" + list.formatByte;
			EditorGUILayout.HelpBox(strInfo, MessageType.Info);
		}

		private void Update()
		{
			if (!m_delta.Equals(0f))
			{
				m_vScrollPos.y -= (m_delta*Time.deltaTime);
				m_vScrollPos.y = Math.Max(0, m_vScrollPos.y);
				m_delta *= 0.9f;
				Repaint();
			}
		}

		private void OnGUIAssets()
		{
			const int CONST_BAR_WIDTH = 55;
			const int CONST_RIGHT_SPACE = 30;

			var names = new string[(int)eSort.Num]
			{
				"name",
				"size",
			};

			var button_width = new float[(int)eSort.Num]
			{
				position.width-(CONST_BAR_WIDTH+CONST_RIGHT_SPACE),
				(CONST_BAR_WIDTH+CONST_RIGHT_SPACE),
			};

			var sortTbl = new eSort[(int)eSort.Num]
			{
				eSort.Name,
				eSort.Size,
			};

			int selectMenuNo = -1;

			using (new GUILayout.HorizontalScope(EditorStyles.toolbar))
			{
				for (int i = 0; i < names.Length; ++i)
				{
					if (GUILayout.Button(names[i], EditorStyles.toolbarButton))
					{
						selectMenuNo = i;
						break;
					}
				}
			}

			var list = GetSelectAssetList();
			if (-1 != selectMenuNo && eSort.None != sortTbl[selectMenuNo])
			{
				eSort sort = sortTbl[selectMenuNo];
				list.Sort(sort, !list.IsSortByAscendingOrder(sort));
			}

			Event currentEvent = Event.current;
			int tmpSelected = m_nSelected;

			if (currentEvent.type == EventType.MouseDrag &&
				new Rect(0,64,position.width-24,position.height-150).Contains(currentEvent.mousePosition))
			{
				m_delta = currentEvent.delta.y * 2.5f;
			}

			using (var scroll = new EditorGUILayout.ScrollViewScope(m_vScrollPos))
			{
				const int CONST_ADD_Y = 16;

				m_vScrollPos = scroll.scrollPosition;

				float top = m_vScrollPos.y - 10;
				float bottom = m_vScrollPos.y + position.height - 100;
				float start_y = (m_vScrollPos.y / 20000);

				var barBgColor = new Color(0, 0, 0, 0.05f);
				var r = new Rect(0, 0, position.width, 16);

				float pos_y = start_y;

				for (int i = 0; i < int.MaxValue; i += 2, pos_y += (CONST_ADD_Y << 1))
				{
					if (pos_y < top)
					{
						continue;
					}
					else if (pos_y > bottom)
					{
						break;
					}

					// 横線表示 .
					r.y = pos_y;
					EditorGUI.DrawRect(r, barBgColor);
				}

				if (0 <= m_nSelected && list.assets.Count > m_nSelected)
				{
					r.y = start_y + CONST_ADD_Y * m_nSelected;
					r.width = position.width;
					if (currentEvent.type == EventType.MouseUp && 1 == currentEvent.button && r.Contains(currentEvent.mousePosition))
					{
						var menu = new GenericMenu();
						menu.AddItem(new GUIContent("Finder"), false, OnClickedContextMenu, "a");
						menu.ShowAsContext();
						currentEvent.Use();
					}

					EditorGUI.DrawRect(r, new Color(0, 1, 0, 0.1f));
				}

				var labelStyle = new GUIStyle(EditorStyles.label);
				labelStyle.alignment = TextAnchor.MiddleRight;

				var btnStyle = new GUIStyle(EditorStyles.label);
				btnStyle.active.textColor = btnStyle.normal.textColor;

				pos_y = start_y;

				r.x = button_width[(int)eSort.Name] + 8;
				for (int i = 0; i < list.assets.Count; ++i, pos_y += CONST_ADD_Y)
				{
					if (pos_y < top)
					{
						continue;
					}
					else if (pos_y > bottom)
					{
						break;
					}

					var asset = list.assets[i];

					r.y = pos_y;
					r.x = 0;
					r.width = r.height;
					GUI.DrawTexture(r, asset.texture);

					r.x = 16;
					r.width = position.width;
					if (GUI.Button(r, asset.name, btnStyle))
					{
						m_delta = 0f;
						m_nSelected = i;
					}

					r.x = button_width[(int)eSort.Name] + 8;
					r.width = CONST_BAR_WIDTH;
					GUI.Label(r, asset.formatByte, labelStyle);
				}

				GUILayout.Label(string.Empty, GUILayout.Height(list.assets.Count * 16 + 4));
			}

			if (tmpSelected != m_nSelected && 0 <= m_nSelected && list.assets.Count > m_nSelected)
			{
				UObject obj = AssetDatabase.LoadAssetAtPath(list.assets[m_nSelected].path, typeof(UObject)) as UObject;
				EditorGUIUtility.PingObject(obj);
				Selection.activeObject = obj;
			}
		}

		private void Save()
		{
			var path = EditorUtility.SaveFilePanel("Save csv...", Application.dataPath + "/../", "FileList" + System.DateTime.Now.ToString("yyyyMMddHHmmss"), "csv");

			if (string.IsNullOrEmpty(path))
			{
				return;
			}

			System.Text.StringBuilder sb = new System.Text.StringBuilder();

			var info = GetSelectAssetList();
			for (int i = 0; i < info.assets.Count; ++i)
			{
				Asset asset = info.assets[i];

				sb.Append((i + 1) + "," + System.IO.Path.GetDirectoryName(asset.path) + "," + System.IO.Path.GetFileName(asset.path) + "," + asset.size + System.Environment.NewLine);
				EditorUtility.DisplayProgressBar("csvファイル作成中", asset.path, (float)i / info.assets.Count);
			}

			EditorUtility.ClearProgressBar();

			File.WriteAllText(path, sb.ToString());

			if (EditorUtility.DisplayDialog("csv出力", path + "ファイルを出力しました", "ブラウザで開く", "閉じる"))
			{
				EditorUtility.RevealInFinder(path);
			}
		}

		private void Read()
		{
			Clear();

			var folderPath = "Assets";
			var fs = System.IO.Directory.GetFiles(folderPath, "*", System.IO.SearchOption.AllDirectories);
			for (int i = 0; i < fs.Length; ++i)
			{
				var extension = Extension2Key(System.IO.Path.GetExtension(fs[i]));
				if (IsIgnoreExtensiton(extension))
				{
					continue;
				}

				GetInfo(extension)?.Add(new Asset(fs[i]));

				EditorUtility.DisplayProgressBar("読込", fs[i], (float)i / fs.Length);
			}

			EditorUtility.ClearProgressBar();

			// カテゴリ追加 .
			var categorys = m_dicInfo.Keys.ToArray();
			if (categorys == null || categorys.Length <= 0)
			{
				categorys = new string[1];
			}

			Array.Sort(categorys);

			var count = (categorys.Length / 32) + 1;
			m_masks = new Mask[count];
			for (var i = 0; i < count; ++i)
			{
				m_masks[i] = new Mask(i, categorys);
				ChangeCategory(i, -1);
			}

			m_isUpdateSelectInfo = true;
		}

		private void OnClickedContextMenu(object obj)
		{
			var path = Application.dataPath.Substring(0, Application.dataPath.Length - ("Assets").Length);
			path += GetSelectAssetList().assets[m_nSelected].path;

			EditorUtility.RevealInFinder(path);
		}

		private bool IsIgnoreExtensiton(string extension)
		{
			if (string.IsNullOrEmpty(extension)
				|| "meta" == extension || "ds_store" == extension
				|| "ignore" == extension || "gitkeep" == extension
				|| "rsp" == extension)
			{
				return true;
			}

			return false;
		}

		private void ChangeCategory(int index, int mask)
		{
			if (m_masks[index].mask != mask)
			{
				m_masks[index].mask = mask;
				m_isUpdateSelectInfo = true;
			}
		}

		AssetList GetSelectAssetList()
		{
			if (!m_isUpdateSelectInfo)
			{
				return m_selectInfo;
			}

			m_nSelected = -1;

			var tmpSorted = m_selectInfo.sorted;
			var tmpOrder = m_selectInfo.IsSortByAscendingOrder(tmpSorted);

			m_selectInfo.Clear();
			for (var i = 0; i < m_masks.Length; ++i)
			{
				var mask = m_masks[i];
				for (var k = 0; k < mask.categorys.Length; ++k)
				{
					if ((mask.mask & (1 << k)) == 0)
					{
						continue;
					}

					var info = GetInfo(mask.categorys[k]);
					if (info != null)
					{
						foreach (var asset in info.assets)
						{
							m_selectInfo.Add(asset);
						}
					}
				}
			}

			m_selectInfo.Setup();
			m_selectInfo.Sort(tmpSorted == eSort.None ? eSort.Name : tmpSorted, tmpSorted == eSort.None ? true : tmpOrder);

			m_vScrollPos = Vector2.zero;

			m_isUpdateSelectInfo = false;

			return m_selectInfo;
		}

		private string Extension2Key(string extension)
		{
			if (string.IsNullOrEmpty(extension))
			{
				return string.Empty;
			}

			return extension.Replace(".", string.Empty).ToLower();
		}

		private AssetList GetInfo(string key)
		{
			if (string.IsNullOrEmpty(key))
			{
				return null;
			}

			if (!m_dicInfo.ContainsKey(key))
			{
				m_dicInfo.Add(key, new AssetList());
			}

			return m_dicInfo[key];
		}

		private void Clear()
		{
			m_isUpdateSelectInfo = true;
			m_delta = 0f;
			m_nSelected = -1;
			m_vScrollPos = Vector2.zero;
			m_masks = new Mask[] { new Mask() };
			m_selectInfo.Clear();

			foreach (var info in m_dicInfo.Values)
			{
				info.Clear();
			}

			m_dicInfo.Clear();
		}

		static MethodInfo GetStorageMemorySize
		{
			get
			{
				if (null == s_getStorageMemorySize)
				{
					var type = Assembly.Load("UnityEditor.dll").GetType("UnityEditor.TextureUtil");
					s_getStorageMemorySize = type.GetMethod("GetStorageMemorySize");
				}
				return s_getStorageMemorySize;
			}
		}

		static long GetAssetsSize(string assetPath)
		{
			var obj = AssetDatabase.LoadAssetAtPath(assetPath, typeof(UObject));
			if (obj is Texture || obj is Texture2D)
			{
				return GetStorageSize(obj);
			}
			else if (obj)
			{
				return Profiler.GetRuntimeMemorySizeLong(obj);
			}

			return 0L;
		}

		static long GetStorageSize(UObject obj)
		{
			return (int)GetStorageMemorySize.Invoke(null, new object[] { obj });
		}
	}
}
