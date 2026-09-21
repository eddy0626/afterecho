using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace Afterecho.Editor
{
    /// <summary>Applies Gameaify art without baking labels or timing into artwork.</summary>
    public static class RunnerUiStyling
    {
        public const string AtlasPath="Assets/Afterecho/Runner/UI/Gameaify_ArcadeUI.png";
        public static string Apply()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play first");
            var v=UnityEngine.Object.FindFirstObjectByType<RunnerView>();
            if(v==null||v.gameObject.scene.path!=RunnerSceneBuilder.ScenePath)throw new InvalidOperationException("Open the RunnerCircle scene");
            var sprites=AssetDatabase.LoadAllAssetsAtPath(AtlasPath).OfType<Sprite>().ToArray();
            Sprite S(string name)=>sprites.Single(s=>s.name==name);
            void Skin(Image image,string name,float sliceScale)
            {image.sprite=S(name);image.type=Image.Type.Sliced;image.color=Color.white;image.pixelsPerUnitMultiplier=sliceScale;image.fillCenter=true;}
            Skin(v.overlay.GetComponent<Image>(),"MenuFrame",2.4f);
            foreach(var b in new[]{v.primary,v.secondary})
            {
                Skin(b.GetComponent<Image>(),"ActionPlate",2f);
                var label=b.GetComponentInChildren<TMPro.TMP_Text>();label.rectTransform.anchoredPosition=new Vector2(-9,3);label.fontStyle=TMPro.FontStyles.Bold;
            }
            v.primary.GetComponent<RectTransform>().sizeDelta=new Vector2(344,61);
            v.secondary.GetComponent<RectTransform>().sizeDelta=new Vector2(344,51);
            v.primary.GetComponent<RectTransform>().anchoredPosition=new Vector2(0,41);
            v.secondary.GetComponent<RectTransform>().anchoredPosition=new Vector2(0,-22);
            foreach(var b in v.difficultyButtons)Skin(b.GetComponent<Image>(),"ActionPlate",4.5f);
            var frame=v.hpFill.transform.parent.GetComponent<Image>();Skin(frame,"HealthHousing",3f);
            frame.rectTransform.sizeDelta=new Vector2(324,39);
            v.hpFill.rectTransform.sizeDelta=new Vector2(286,15);
            v.hpFill.rectTransform.anchoredPosition=new Vector2(0,1);
            v.hpFill.color=new Color(.12f,.85f,.91f);
            // Persist the texture before creating its sprite: legacy Square has a null texture reference.
            const string whitePath="Assets/Afterecho/Runner/UI/RunnerWhite.asset";
            var white=AssetDatabase.LoadAllAssetsAtPath(whitePath).OfType<Sprite>().FirstOrDefault();
            if(white==null)
            {
                var texture=new Texture2D(2,2,TextureFormat.RGBA32,false){name="RunnerWhiteTexture"};
                texture.SetPixels(new[]{Color.white,Color.white,Color.white,Color.white});texture.Apply();
                AssetDatabase.CreateAsset(texture,whitePath);
                white=Sprite.Create(texture,new Rect(0,0,2,2),new Vector2(.5f,.5f),100);white.name="RunnerWhite";
                AssetDatabase.AddObjectToAsset(white,texture);AssetDatabase.SaveAssets();
            }
            if(white.texture==null)throw new InvalidOperationException("HP fill texture is missing");
            v.hpFill.sprite=white;v.progressFill.sprite=white;
            v.healthRoot.anchoredPosition=new Vector2(0,-175);
            v.hpText.rectTransform.anchoredPosition=new Vector2(0,-29);
            // Reserve the center for the timing UI and an outer strip for the runner.
            v.track.offsetMin=Vector2.one*32;v.track.offsetMax=-Vector2.one*32;
            v.runnerVisual.sizeDelta=new Vector2(72,72);v.runnerImage.rectTransform.sizeDelta=new Vector2(72,72);
            v.score.rectTransform.anchoredPosition=new Vector2(185,-16);v.score.fontSize=18;
            v.modeLabel.rectTransform.anchoredPosition=new Vector2(190,14);
            v.progress.rectTransform.anchoredPosition=new Vector2(-213,14);
            ((RectTransform)v.progressFill.transform.parent).anchoredPosition=new Vector2(-183,-16);
            v.combo.rectTransform.anchoredPosition=new Vector2(0,185);v.combo.fontSize=42;
            v.boostText.rectTransform.anchoredPosition=new Vector2(0,148);
            var existing=v.safeArea.Find("OverdriveBadge");
            var badge=existing?existing.gameObject:new GameObject("OverdriveBadge",typeof(RectTransform),typeof(Image));
            var rect=badge.GetComponent<RectTransform>();rect.SetParent(v.safeArea,false);rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.anchoredPosition=new Vector2(-151,185);rect.sizeDelta=new Vector2(46,46);
            v.overdriveBadge=badge.GetComponent<Image>();v.overdriveBadge.sprite=S("OverdriveBadge");v.overdriveBadge.preserveAspect=true;v.overdriveBadge.raycastTarget=false;badge.SetActive(false);
            v.overlay.transform.SetAsLastSibling();
            EditorUtility.SetDirty(v);EditorSceneManager.MarkSceneDirty(v.gameObject.scene);EditorSceneManager.SaveScene(v.gameObject.scene);AssetDatabase.SaveAssets();
            return "Gameaify menu, action plates, health housing and boost badge applied";
        }
    }
}
