UnityEditor.AssetDatabase.Refresh();
if(UnityEditor.EditorApplication.isPlaying)throw new System.Exception("Stop Play first");
Afterecho.Editor.RunnerSceneBuilder.RefreshArt();
var v=UnityEngine.Object.FindAnyObjectByType<Afterecho.RunnerView>();
string folder="Assets/Afterecho/Runner/Animations";System.IO.Directory.CreateDirectory(folder);UnityEditor.AssetDatabase.Refresh();
string[] names={"Run","Sprint","Fall","Recover"};UnityEngine.Sprite[][] frames={v.runFrames,v.sprintFrames,v.fallFrames,v.recoverFrames};float[] durations={1f/1.8f,1f/3.6f,.35f*.57f,.35f*.43f};
for(int k=0;k<4;k++){
 string path=folder+"/"+names[k]+".anim";var clip=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.AnimationClip>(path);if(clip==null){clip=new UnityEngine.AnimationClip();UnityEditor.AssetDatabase.CreateAsset(clip,path);}clip.frameRate=60;
 var keys=new System.Collections.Generic.List<UnityEditor.ObjectReferenceKeyframe>();
 for(int i=0;i<frames[k].Length;i++)keys.Add(new UnityEditor.ObjectReferenceKeyframe{time=i*durations[k]/frames[k].Length,value=frames[k][i]});
 keys.Add(new UnityEditor.ObjectReferenceKeyframe{time=durations[k],value=frames[k][k<2?0:frames[k].Length-1]});
 UnityEditor.AnimationUtility.SetObjectReferenceCurve(clip,UnityEditor.EditorCurveBinding.PPtrCurve("",typeof(UnityEngine.UI.Image),"m_Sprite"),keys.ToArray());var settings=UnityEditor.AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=k<2;UnityEditor.AnimationUtility.SetAnimationClipSettings(clip,settings);UnityEditor.EditorUtility.SetDirty(clip);
}
foreach(string guid in UnityEditor.AssetDatabase.FindAssets("t:AudioClip",new[]{"Assets/Afterecho/Runner/Audio"})){
 var importer=(UnityEditor.AudioImporter)UnityEditor.AssetImporter.GetAtPath(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));var settings=importer.defaultSampleSettings;settings.loadType=UnityEngine.AudioClipLoadType.DecompressOnLoad;settings.compressionFormat=UnityEngine.AudioCompressionFormat.PCM;settings.preloadAudioData=true;settings.sampleRateSetting=UnityEditor.AudioSampleRateSetting.PreserveSampleRate;importer.defaultSampleSettings=settings;importer.SaveAndReimport();
}
UnityEditor.AssetDatabase.SaveAssets();return new{run=v.runFrames.Length,sprint=v.sprintFrames.Length,fall=v.fallFrames.Length,recover=v.recoverFrames.Length};
