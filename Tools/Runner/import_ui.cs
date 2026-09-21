UnityEditor.AssetDatabase.Refresh();
string path="Assets/Afterecho/Runner/UI/Gameaify_ArcadeUI.png";
var importer=(UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(path);
importer.textureType=UnityEditor.TextureImporterType.Sprite;importer.spriteImportMode=UnityEditor.SpriteImportMode.Multiple;importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.textureCompression=UnityEditor.TextureImporterCompression.Uncompressed;importer.maxTextureSize=1024;importer.SaveAndReimport();
var factories=new UnityEditor.U2D.Sprites.SpriteDataProviderFactories();factories.Init();var provider=factories.GetSpriteEditorDataProviderFromObject(importer);provider.InitSpriteEditorDataProvider();
var capability=provider.GetDataProvider<UnityEditor.U2D.Sprites.ISpriteFrameEditCapability>();
if(capability==null)throw new System.Exception("Sprite editing unsupported; aborted");
var flags=capability.GetEditCapability();
foreach(var required in new[]{UnityEditor.U2D.Sprites.EEditCapability.EditSpriteName,UnityEditor.U2D.Sprites.EEditCapability.EditSpriteRect,UnityEditor.U2D.Sprites.EEditCapability.EditBorder,UnityEditor.U2D.Sprites.EEditCapability.EditPivot,UnityEditor.U2D.Sprites.EEditCapability.CreateAndDeleteSprite})
 if(!flags.HasCapability(required))throw new System.Exception("Sprite capability unavailable: "+required);
string[] names={"MenuFrame","ActionPlate","HealthHousing","OverdriveBadge"};
UnityEngine.Rect[] bounds={new UnityEngine.Rect(70,470,398,511),new UnityEngine.Rect(549,653,454,147),new UnityEngine.Rect(26,180,474,115),new UnityEngine.Rect(627,100,293,289)};
UnityEngine.Vector4[] borders={new UnityEngine.Vector4(50,45,65,48),new UnityEngine.Vector4(45,36,70,28),new UnityEngine.Vector4(48,25,45,25),UnityEngine.Vector4.zero};
var old=provider.GetSpriteRects();var rects=new UnityEditor.SpriteRect[4];
for(int i=0;i<4;i++){
 var previous=System.Linq.Enumerable.FirstOrDefault(old,s=>s.name==names[i]);
 rects[i]=new UnityEditor.SpriteRect{name=names[i],rect=bounds[i],border=borders[i],pivot=new UnityEngine.Vector2(.5f,.5f),alignment=UnityEngine.SpriteAlignment.Center,spriteID=previous==null?UnityEngine.GUID.Generate():previous.spriteID};
}
provider.SetSpriteRects(rects);
var nameProvider=provider.GetDataProvider<UnityEditor.U2D.Sprites.ISpriteNameFileIdDataProvider>();
if(nameProvider!=null)nameProvider.SetNameFileIdPairs(System.Linq.Enumerable.Select(rects,r=>new UnityEditor.SpriteNameFileIdPair(r.name,r.spriteID)));
provider.Apply();importer.SaveAndReimport();
return System.Linq.Enumerable.Select(UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path),a=>a.name).ToArray();
