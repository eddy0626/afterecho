string src="Assets/Settings/Build Profiles/Afterecho_Web_Team_0913 - Desktop - Release.asset",dst="Assets/Settings/Build Profiles/Afterecho_RunnerCircle_Web_Test.asset";
if(UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Build.Profile.BuildProfile>(dst)==null)UnityEditor.AssetDatabase.CopyAsset(src,dst);
var p=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Build.Profile.BuildProfile>(dst);p.name="Afterecho_RunnerCircle_Web_Test";p.overrideGlobalScenes=true;p.scenes=new[]{new UnityEditor.EditorBuildSettingsScene(Afterecho.Editor.RunnerSceneBuilder.ScenePath,true)};
UnityEditor.EditorUtility.SetDirty(p);UnityEditor.AssetDatabase.SaveAssets();return dst;
