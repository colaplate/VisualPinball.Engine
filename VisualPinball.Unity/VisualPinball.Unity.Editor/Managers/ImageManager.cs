// Visual Pinball Engine
// Copyright (C) 2021 freezy and VPE Team
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.IO;
using NLog;
using UnityEditor;
using UnityEngine;
using VisualPinball.Engine.VPT;
using Logger = NLog.Logger;

namespace VisualPinball.Unity.Editor
{
	/// <summary>
	/// Editor UI for VPX images, equivalent to VPX's "Image Manager" window
	/// </summary>
	public class ImageManager : ManagerWindow<ImageListData>
	{
		protected override string DataTypeName => "Image";

		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		[MenuItem("Visual Pinball/Image Manager", false, 401)]
		public static void ShowWindow()
		{
			GetWindow<ImageManager>("Image Manager");
		}

		public override void OnEnable()
		{
			titleContent = new GUIContent("Image Manager", EditorGUIUtility.IconContent("RawImage Icon").image);
			base.OnEnable();
		}

		protected override void OnButtonBarGUI() {
			if (GUILayout.Button("Update All", GUILayout.ExpandWidth(false))) {
				UpdateAllImages();
			}
		}

		protected override void OnDataDetailGUI()
		{
			SliderField("Alpha Mask", ref _selectedItem.TextureData.AlphaTestValue, 0, 255);

			EditorGUI.BeginChangeCheck();
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.PrefixLabel("Replace Image");
			var tex = (Texture2D)EditorGUILayout.ObjectField(null, typeof(Texture2D), false);
			EditorGUILayout.EndHorizontal();
			if (EditorGUI.EndChangeCheck() && tex != null) {
				ReplaceImageFromAsset(_selectedItem.TextureData, tex);
			}
			if (GUILayout.Button("Export Image")) {
				ExportImage();
			}
			if (GUILayout.Button("Export Image as PNG")) {
				ExportImageAsPng();
			}

			var unityTex = _tableAuthoring.GetTexture(_selectedItem.Name);
			if (unityTex != null) {
				var rect = GUILayoutUtility.GetRect(new GUIContent(""), GUIStyle.none);
				float aspect = (float)unityTex.height / unityTex.width;
				rect.width = Mathf.Min(unityTex.width, rect.width);
				rect.height = rect.width * aspect;
				GUI.DrawTexture(rect, unityTex);
			}
		}

		protected override void RenameExistingItem(ImageListData data, string newName)
		{
			string oldName = data.TextureData.Name;

			// give each editable item a chance to update its fields
			string undoName = "Rename Image";
			foreach (var item in _tableAuthoring.GetComponentsInChildren<IItemMeshAuthoring>()) {
				RenameReflectedFields(undoName, item, item.TextureRefs, oldName, newName);
			}
			RecordUndo(undoName, data.TextureData);

			data.TextureData.Name = newName;
		}

		protected override List<ImageListData> CollectData()
		{
			List<ImageListData> data = new List<ImageListData>();

			// collect list of in use textures
			List<string> inUseTextures = new List<string>();
			foreach (var item in _tableAuthoring.GetComponentsInChildren<IItemMeshAuthoring>()) {
				var texRefs = item.TextureRefs;
				if (texRefs == null) { continue; }
				foreach (var texRef in texRefs) {
					var texName = GetMemberValue(texRef, item.ItemData);
					if (!string.IsNullOrEmpty(texName)) {
						inUseTextures.Add(texName);
					}
				}
			}

			foreach (var t in _tableAuthoring.Textures) {
				var texData = t.Data;
				data.Add(new ImageListData { TextureData = texData, InUse = inUseTextures.Contains(texData.Name)});
			}

			return data;
		}

		protected override void OnDataChanged(string undoName, ImageListData data)
		{
			OnDataChanged(undoName, data.TextureData);
		}

		protected override void AddNewData(string undoName, string newName) {
			Undo.RecordObject(_tableAuthoring, undoName);

			var newTex = new Engine.VPT.Texture(newName);
			_tableAuthoring.Textures.Add(newTex);
			_tableAuthoring.Item.Data.NumTextures = _tableAuthoring.Textures.Count;
		}

		protected override void RemoveData(string undoName, ImageListData data)
		{
			Undo.RecordObject(_tableAuthoring, undoName);

			_tableAuthoring.Textures.Remove(data.Name);
			_tableAuthoring.Item.Data.NumTextures = _tableAuthoring.Textures.Count;
		}

		private void OnDataChanged(string undoName, TextureData textureData)
		{
			RecordUndo(undoName, textureData);

			// update any items using this tex
			foreach (var item in _tableAuthoring.GetComponentsInChildren<IItemMeshAuthoring>()) {
				if (IsReferenced(item.TextureRefs, item.ItemData, textureData.Name)) {
					item.MeshDirty = true;
					Undo.RecordObject(item as UnityEngine.Object, undoName);
				}
			}
		}

		private void RecordUndo(string undoName, TextureData textureData)
		{
			if (_tableAuthoring == null) { return; }

			// Run over table's texture scriptable object wrappers to find the one being edited and add to the undo stack
			foreach (var tableTex in _tableAuthoring.Textures.SerializedObjects) {
				if (tableTex.Data == textureData) {
					Undo.RecordObject(tableTex, undoName);
					break;
				}
			}
		}

		private void UpdateAllImages()
		{
			int countFound = 0;
			foreach (var t in _tableAuthoring.Textures) {
				if (File.Exists(t.Data.Path)) {
					countFound++;
					ReplaceImageFromPath(t.Data, t.Data.Path);
				}
			}
			Logger.Info($"Update all images complete. Found files for {countFound} / {_tableAuthoring.Textures.Count}");
		}

		private void ReplaceImageFromPath(TextureData textureData, string path) {
			if (_tableAuthoring == null || textureData == null || string.IsNullOrEmpty(path)) { return; }

			byte[] newBytes = null;
			try {
				newBytes = File.ReadAllBytes(path);
			} catch (Exception ex) {
				Logger.Error(ex);
			}
			if (newBytes == null) { return; }

			string undoName = "Replace Image";

			_tableAuthoring.MarkDirty<Engine.VPT.Texture>(textureData.Name);
			Undo.RecordObject(_tableAuthoring, undoName);
			OnDataChanged(undoName, textureData);

			textureData.Binary.Data = newBytes;
			textureData.Binary.Size = newBytes.Length;
			textureData.Path = path;

			// update size values assuming we loaded alright
			var unityTex = _tableAuthoring.GetTexture(textureData.Name);
			if (unityTex != null) {
				textureData.Width = unityTex.width;
				textureData.Height = unityTex.height;
			}
		}

		private void ReplaceImageFromAsset(TextureData textureData, Texture2D tex)
		{
			string path = AssetDatabase.GetAssetPath(tex);
			if (!string.IsNullOrEmpty(path)) {
				ReplaceImageFromPath(textureData, path);
			}
		}

		private void ExportImage()
		{
			if (_tableAuthoring == null || _selectedItem == null) { return; }

			var unityTex = _tableAuthoring.GetTexture(_selectedItem.TextureData.Name);
			if (unityTex != null) {
				string fileExt = Path.GetExtension(_selectedItem.TextureData.Path).TrimStart('.');
				if (string.IsNullOrEmpty(fileExt)) {
					Logger.Error("Could not determine filetype from path");
				}
				string savePath = EditorUtility.SaveFilePanelInProject("Export Image", unityTex.name, fileExt, "Export Image");
				if (!string.IsNullOrEmpty(savePath)) {
					File.WriteAllBytes(savePath, _selectedItem.TextureData.Binary.Data);
					AssetDatabase.ImportAsset(savePath);
				}
			}
		}

		private void ExportImageAsPng()
		{
			if (_tableAuthoring == null || _selectedItem == null) { return; }

			var unityTex = _tableAuthoring.GetTexture(_selectedItem.TextureData.Name);
			if (unityTex != null) {
				string savePath = EditorUtility.SaveFilePanelInProject("Export Image", unityTex.name, "png", "Export Image");
				if (!string.IsNullOrEmpty(savePath)) {
					File.WriteAllBytes(savePath, unityTex.EncodeToPNG());
					AssetDatabase.ImportAsset(savePath);
				}
			}
		}
	}
}
