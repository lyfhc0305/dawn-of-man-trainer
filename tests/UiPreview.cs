using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Text;
using DawnTrainer;

internal sealed class UiHit
{
    internal UiRect Rect;
    internal string Id;
    internal bool Enabled;
}

internal sealed class UiText
{
    internal UiRect Rect;
    internal string Text;
}

// Offline implementation of the same canvas contract used in Unity. This never loads Unity.
internal sealed class PreviewCanvas : UiCanvas, IDisposable
{
    internal readonly List<UiHit> Hits = new List<UiHit>();
    internal readonly List<UiText> Texts = new List<UiText>();
    internal readonly Dictionary<string,string> Inputs = new Dictionary<string,string>();
    internal string ClickId;
    internal string FocusId;
    internal float? NextSlider;
    internal readonly Dictionary<string,float> NextSliders = new Dictionary<string,float>();
    internal readonly List<string> SliderCalls = new List<string>();
    private bool consumed;
    private readonly Bitmap bitmap;
    private readonly Graphics graphics;
    private readonly Dictionary<string,Font> fonts = new Dictionary<string,Font>();
    private readonly Dictionary<string,System.Drawing.Image> images = new Dictionary<string,System.Drawing.Image>();
    private readonly Stack<GraphicsState> states = new Stack<GraphicsState>();
    private readonly Stack<RectangleF> clips = new Stack<RectangleF>();
    private readonly Stack<PointF> offsets = new Stack<PointF>();
    private PointF offset;
    internal PreviewCanvas(int height=800)
    {
        bitmap = new Bitmap((int)TrainerView.Width,height);
        graphics = Graphics.FromImage(bitmap); graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.Clear(Color.FromArgb(23,24,27));
    }
    private static RectangleF Rect(UiRect r) { return new RectangleF(r.X,r.Y,r.W,r.H); }
    private static Color ColorOf(string value)
    {
        string s=value.TrimStart('#'); int r=Int32.Parse(s.Substring(0,2),NumberStyles.HexNumber),g=Int32.Parse(s.Substring(2,2),NumberStyles.HexNumber),b=Int32.Parse(s.Substring(4,2),NumberStyles.HexNumber);
        return Color.FromArgb(s.Length==8?Int32.Parse(s.Substring(6,2),NumberStyles.HexNumber):255,r,g,b);
    }
    private Font FontOf(int size,bool bold)
    {
        string key=size+":"+bold;Font font;
        if(!fonts.TryGetValue(key,out font)){font=new Font("Microsoft YaHei UI",size,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel);fonts[key]=font;}
        return font;
    }
    private static GraphicsPath Rounded(RectangleF r,float radius)
    {
        GraphicsPath path=new GraphicsPath();float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));
        if(d<.2f){path.AddRectangle(r);return path;}
        path.AddArc(r.X,r.Y,d,d,180,90);path.AddArc(r.Right-d,r.Y,d,d,270,90);
        path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.X,r.Bottom-d,d,d,90,90);path.CloseFigure();return path;
    }
    internal override void Fill(UiRect r,string color,float radius)
    {
        if(r.W<=0||r.H<=0)return;
        using(GraphicsPath path=Rounded(Rect(r),radius))using(SolidBrush brush=new SolidBrush(ColorOf(color)))graphics.FillPath(brush,path);
    }
    internal override void Text(UiRect r,string text,int size,string color,bool bold,int align)
    {
        Texts.Add(new UiText{Rect=new UiRect(r.X+offset.X,r.Y+offset.Y,r.W,r.H),Text=text??""});
        using(SolidBrush brush=new SolidBrush(ColorOf(color)))using(StringFormat format=new StringFormat(StringFormat.GenericTypographic))
        {
            format.Alignment=align==1?StringAlignment.Center:align==2?StringAlignment.Far:StringAlignment.Near;
            format.LineAlignment=StringAlignment.Center;format.FormatFlags=StringFormatFlags.NoWrap;
            format.Trimming=StringTrimming.EllipsisCharacter;
            graphics.DrawString(text??"",FontOf(size,bold),brush,Rect(r),format);
        }
    }
    internal override void Line(float x1,float y1,float x2,float y2,string color,float width)
    {using(Pen pen=new Pen(ColorOf(color),width)){pen.StartCap=pen.EndCap=LineCap.Round;graphics.DrawLine(pen,x1,y1,x2,y2);}}
    internal override void Image(UiRect r,object image)
    {
        string path=image as string;if(String.IsNullOrEmpty(path)||!File.Exists(path))return;
        System.Drawing.Image bitmap;
        if(!images.TryGetValue(path,out bitmap)){bitmap=System.Drawing.Image.FromFile(path);images[path]=bitmap;}
        float scale=Math.Min(r.W/bitmap.Width,r.H/bitmap.Height),w=bitmap.Width*scale,h=bitmap.Height*scale;
        graphics.DrawImage(bitmap,r.X+(r.W-w)/2,r.Y+(r.H-h)/2,w,h);
    }
    internal override bool Hit(UiRect r,string id,bool enabled)
    {
        RectangleF absolute=new RectangleF(r.X+offset.X,r.Y+offset.Y,r.W,r.H);
        if(clips.Count>0)absolute=RectangleF.Intersect(absolute,clips.Peek());
        if(absolute.Width>0&&absolute.Height>0)Hits.Add(new UiHit{Id=id,Enabled=enabled,Rect=new UiRect(absolute.X,absolute.Y,absolute.Width,absolute.Height)});
        if(!consumed&&enabled&&id==ClickId){consumed=true;return true;}return false;
    }
    internal override bool Hover(UiRect r){return false;}
    internal override bool Focused(string id){return FocusId==id;}
    internal override string Input(UiRect r,string id,string value,int maximumLength,bool centered)
    {
        Hit(r,"input."+id,true);
        string result;if(!Inputs.TryGetValue(id,out result))result=value;
        if(result.Length>maximumLength)result=result.Substring(0,maximumLength);
        Text(r.Inset(4),result,centered?16:14,TrainerView.TextMain,false,centered?1:0);return result;
    }
    internal override float Slider(UiRect r,string id,float value,float min,float max)
    {SliderCalls.Add(id);Hit(r,"slider."+id,true);float next;return NextSliders.TryGetValue(id,out next)?next:NextSlider??value;}
    internal override float BeginScroll(UiRect viewport,float position,float contentHeight)
    {
        position=Math.Max(0,Math.Min(position,Math.Max(0,contentHeight-viewport.H)));
        if(contentHeight>viewport.H)
        {
            Fill(new UiRect(viewport.X+viewport.W-5,viewport.Y,5,viewport.H),"#181A1F",2);
            float h=Math.Max(28,viewport.H*viewport.H/contentHeight),y=position/(contentHeight-viewport.H)*(viewport.H-h);
            Fill(new UiRect(viewport.X+viewport.W-5,viewport.Y+y,5,h),"#474E5D",2);
        }
        states.Push(graphics.Save()); offsets.Push(offset);
        RectangleF clip=new RectangleF(viewport.X+offset.X,viewport.Y+offset.Y,viewport.W-10,viewport.H);
        if(clips.Count>0)clip=RectangleF.Intersect(clip,clips.Peek());clips.Push(clip);
        graphics.SetClip(Rect(viewport),CombineMode.Intersect);graphics.TranslateTransform(viewport.X,viewport.Y-position);
        offset=new PointF(offset.X+viewport.X,offset.Y+viewport.Y-position);return position;
    }
    internal override void EndScroll(){graphics.Restore(states.Pop());clips.Pop();offset=offsets.Pop();}
    internal override float Measure(string text,int size,bool bold)
    {using(StringFormat format=new StringFormat(StringFormat.GenericTypographic))return graphics.MeasureString(text,FontOf(size,bold),Int32.MaxValue,format).Width;}
    internal void Save(string path){bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);}
    internal void SaveAtResolution(string path,int width,int height)
    {
        using(Bitmap screen=new Bitmap(width,height))using(Graphics g=Graphics.FromImage(screen))
        {
            g.Clear(Color.FromArgb(12,14,18));g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            float scale=FeaturePolicy.PanelScale(width,height,TrainerView.Width,TrainerView.Height,100);
            float w=TrainerView.Width*scale,h=bitmap.Height*scale;
            g.DrawImage(bitmap,(width-w)/2,(height-h)/2,w,h);screen.Save(path,System.Drawing.Imaging.ImageFormat.Png);
        }
    }
    public void Dispose(){foreach(Font f in fonts.Values)f.Dispose();foreach(System.Drawing.Image image in images.Values)image.Dispose();graphics.Dispose();bitmap.Dispose();}
}

internal static class UiPreview
{
    private static int passed,failed;
    private static void Check(bool condition,string message){if(condition){passed++;Console.WriteLine("PASS "+message);}else{failed++;Console.WriteLine("FAIL "+message);}}
    internal static UiModel Model(string catalog)
    {
        UiModel m=new UiModel{GameVersion="1.8.3",Ready=true,Knowledge=128,SelectionCount=3,BuildTargets=2,KillTargets=1,SelectedResource="raw_meat",SelectedAnimal="deer",StatusError=false,Weather=0,SelectionName="鹿",TaskName="生肉",Status="准备就绪，修改仅作用于当前地图。"};
        m.RescueTargets=2;m.RepairTargets=1;m.ResourceRepairTargets=1;m.TameTargets=1;m.HousingFree=18;m.MigrantCount=m.TraderCount=0;
        m.CapturingKey=-1;m.AutoPauseActive=false;
        for(int i=0;i<7;i++)m.TargetStats[i]=.48f+i*.07f;
        m.Materials.Add(new SupplyLine{Id="log",Quantity="8"});m.Materials.Add(new SupplyLine{Id="stone",Quantity="6"});
        m.Locks[0]=m.Locks[4]=true;
        if(catalog=="--fixtures")
        {
            // Synthetic catalog exercises long lists without shipping game assets.
            m.Resources.Add(new UiItem{Id="raw_meat",Name="肉",Categories=1});
            string[] ids={"log","sticks","stone","cured_meat","dry_fish","skins_outfit","flint"};string[] names={"原木","树枝","石头","腌肉","干鱼","兽皮装","燧石"};
            for(int i=0;i<ids.Length;i++)m.Resources.Add(new UiItem{Id=ids[i],Name=names[i],Categories=2});
            for(int i=0;i<5;i++)m.Resources.Add(new UiItem{Id="flint_fixture_"+i,Name="燧石测试 "+i,Categories=2});
            for(int i=m.Resources.Count;i<87;i++)m.Resources.Add(new UiItem{Id="resource_fixture_"+i,Name="测试资源 "+i,Categories=1<<(i%4)});
            m.Animals.Add(new UiItem{Id="deer",Name="鹿"});
            for(int i=1;i<31;i++)m.Animals.Add(new UiItem{Id="animal_fixture_"+i,Name="测试动物 "+i});
        }
        else foreach(string line in File.ReadAllLines(catalog,Encoding.UTF8))
        {
            string[] cells=line.Split('\t');if(cells.Length<5)continue;
            UiItem item=new UiItem{Id=cells[1],Name=cells[2],Categories=Int32.Parse(cells[3]),Image=cells[4]};
            (cells[0]=="resource"?m.Resources:m.Animals).Add(item);
        }
        m.ResourceCount=m.Resources.Count;m.AnimalCount=m.Animals.Count;return m;
    }
    private static void GeometryChecks(UiModel model)
    {
        using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,model);Check(c.Texts.Any(t=>t.Text=="Dawn of Man  1.8.3")&&!c.Texts.Any(t=>t.Text.Contains("1.8.4")),"sidebar displays the supplied game version instead of a fixed version");}
        for(int page=0;page<9;page++)
        using(PreviewCanvas c=new PreviewCanvas(TrainerView.PanelHeight(page)))
        {
            model.Page=page;TrainerView.Render(c,model);
            Check(c.Hits.All(h=>h.Rect.X>=0&&h.Rect.Y>=0&&h.Rect.X+h.Rect.W<=TrainerView.Width+.1f&&h.Rect.Y+h.Rect.H<=TrainerView.PanelHeight(page)+.1f),"page "+page+" controls stay in window");
            Check(!Overlaps(c.Hits),"page "+page+" interactive controls do not overlap");
            Check(c.Hits.Where(h=>h.Id.StartsWith("nav.")).All(h=>h.Rect.Y+h.Rect.H<=(page==8?544:TrainerView.Height-178)),"page "+page+" navigation stays clear of connection card");
            Check(c.Texts.All(t=>t.Rect.Y>=0&&t.Rect.Y+t.Rect.H<=TrainerView.PanelHeight(page)+.1f),"page "+page+" text stays within its actual panel height");
        }
        model.Page=5;model.TargetTab=1;
        using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,model);Check(!Overlaps(c.Hits)&&c.Hits.All(h=>h.Rect.X>=0&&h.Rect.Y>=0&&h.Rect.X+h.Rect.W<=TrainerView.Width+.1f&&h.Rect.Y+h.Rect.H<=TrainerView.Height+.1f),"maintenance tab controls stay in window without overlap");}
        model.TargetTab=0;
        model.Page=1;model.Quantity="1001";
        using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,model);Check(c.Hits.Any(h=>h.Id=="spawn"&&!h.Enabled),"oversized resource count disables add");}
        model.Quantity="abc";
        using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,model);Check(c.Hits.Any(h=>h.Id=="spawn"&&!h.Enabled),"nonnumeric resource count disables add");}
        model.Quantity="100";model.Busy=true;
        using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,model);Check(c.Hits.Any(h=>h.Id=="spawn"&&!h.Enabled),"busy generation disables duplicate add");}
        model.Busy=false;model.Quantity="10";model.Search="nothing matches";
        using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,model);Check(c.Hits.Any(h=>h.Id=="filters.clear"),"empty search offers clear filters");}
        model.Search="flint";
        using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,model);Check(c.Hits.Count(h=>h.Id.StartsWith("item."))==6,"live ID filter finds the six flint resources");}
        model.Search="";model.Page=2;model.AnimalQuantity="51";
        using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,model);Check(c.Hits.Any(h=>h.Id=="spawn"&&!h.Enabled),"oversized animal count disables generation");}
        model.AnimalQuantity="1";model.Page=5;model.SelectionCount=model.KillTargets=model.BuildTargets=0;
        bool disabled=true;
        for(int tab=0;tab<2;tab++){model.TargetTab=tab;using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,model);disabled&=c.Hits.Any(h=>h.Id==(tab==0?"kill":"complete")&&!h.Enabled);}}
        Check(disabled,"empty selection disables target action buttons");model.TargetTab=0;
        model.Ready=false;
        using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,model);Check(!c.Hits.Any(h=>h.Id=="kill"||h.Id=="complete"||h.Id=="spawn"),"title/loading page exposes no game actions");}
        model.Ready=true;model.KillTargets=1;model.BuildTargets=2;model.SelectionCount=3;
        model.Page=0;model.Speed=.25f;
        using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,model);Check(c.Texts.Any(t=>t.Text=="0.25×")&&!c.Texts.Any(t=>t.Text=="0.3×"),"overview preserves exact quarter-speed precision");}
        model.AutoPauseActive=true;
        using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,model);Check(c.Texts.Any(t=>t.Text=="面板暂停"&&!c.Texts.Any(v=>v.Text=="0.25×")),"overview summary identifies automatic pause instead of a running speed");}
        model.AutoPauseActive=false;model.Speed=1;
        List<string> actions=new List<string>();model.Action=(a,v)=>actions.Add(a+":"+v);model.Page=1;
        using(PreviewCanvas c=new PreviewCanvas()){c.ClickId="spawn";TrainerView.Render(c,model);Check(actions.SequenceEqual(new[]{"spawn.resource:"}),"resource primary button dispatches its intended action");}
        actions.Clear();model.Page=2;
        using(PreviewCanvas c=new PreviewCanvas()){c.ClickId="spawn";TrainerView.Render(c,model);Check(actions.SequenceEqual(new[]{"spawn.animal:"}),"animal primary button dispatches its intended action");}
        actions.Clear();model.Page=5;model.TargetTab=0;
        using(PreviewCanvas c=new PreviewCanvas()){c.ClickId="kill";TrainerView.Render(c,model);Check(actions.SequenceEqual(new[]{"kill:"}),"danger button dispatches only kill action");}
        actions.Clear();model.KillVillagers="Tarnak";
        using(PreviewCanvas c=new PreviewCanvas()){c.ClickId="kill";TrainerView.Render(c,model);Check(actions.SequenceEqual(new[]{"confirm.arm:kill"})&&model.ArmedAction=="kill"&&c.Texts.Any(t=>t.Text=="含村民 Tarnak"),"killing selected villagers names them and waits for a second click");}
        actions.Clear();
        using(PreviewCanvas c=new PreviewCanvas()){c.ClickId="kill";TrainerView.Render(c,model);Check(actions.SequenceEqual(new[]{"kill:"})&&model.ArmedAction==null,"second click confirms the villager kill");}
        model.ArmedAction="kill";model.Navigate(1);model.Navigate(5);Check(model.ArmedAction==null,"changing page cancels a pending confirmation");model.KillVillagers=null;
        actions.Clear();model.Page=0;
        using(PreviewCanvas c=new PreviewCanvas()){c.ClickId="lock.0";bool before=model.Locks[0];TrainerView.Render(c,model);Check(model.Locks[0]!=before,"custom switch changes its actual model value");}
        actions.Clear();model.Page=1;
        using(PreviewCanvas c=new PreviewCanvas()){c.ClickId="quantity.100";TrainerView.Render(c,model);Check(model.Quantity=="100","quantity chip changes add quantity");}
        model.Action=null;
        for(int page=1;page<=2;page++)
        for(int state=0;state<2;state++)
        {
            model.Page=page;model.Quantity=model.AnimalQuantity=state==0?"0":"10";model.Busy=state==1;model.Done=4;model.Total=10;
            using(PreviewCanvas c=new PreviewCanvas())
            {
                TrainerView.Render(c,model);
                UiText feedback=c.Texts.Single(t=>t.Text.StartsWith(state==0?"请输入 1–":"正在添加 "+model.TaskName));
                Check(!c.Hits.Any(h=>Intersects(feedback.Rect,h.Rect)),"page "+page+" "+(state==0?"invalid count":"generation progress")+" text stays clear of controls");
                Check(!c.Texts.Any(t=>!Object.ReferenceEquals(t,feedback)&&!String.IsNullOrEmpty(t.Text)&&Intersects(feedback.Rect,t.Rect)),"page "+page+" "+(state==0?"invalid count":"generation progress")+" text has a separate row area");
            }
        }
        model.Page=3;model.Busy=false;
        using(PreviewCanvas c=new PreviewCanvas())
        {
            actions.Clear();model.Action=(a,v)=>actions.Add(a+":"+v);c.NextSlider=TrainerView.SpeedTrack(8);TrainerView.Render(c,model);
            Check(actions.Contains("speed:8"),"speed slider dispatches selected speed");
        }
        model.Speed=1.5f;actions.Clear();
        using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,model);Check(!actions.Any(a=>a.StartsWith("speed:")),"an untouched slider never rewrites a speed between its steps");}
        model.Speed=1;
        Check(Math.Abs(TrainerView.SpeedTrack(1)-(TrainerView.SpeedTrack(4)-TrainerView.SpeedTrack(1)))<.001f,"logarithmic slider gives 0.25×–1× and 1×–4× the same width");
        model.Action=null;
        model.Page=1;model.Quantity="10";model.Search="  flint  ";
        using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,model);Check(c.Hits.Count(h=>h.Id.StartsWith("item."))==6,"search ignores leading and trailing spaces");Check(c.Texts.Any(t=>t.Text=="已选项不在当前筛选中"),"filtered-out selection is explicitly identified");Check(c.Texts.Any(t=>t.Text.StartsWith("添加 ")&&t.Text.Contains("× 10")),"primary button identifies the actual item and count");}
        model.ResourceScroll=0;model.Category=2;
        using(PreviewCanvas c=new PreviewCanvas()){c.ClickId="nav.1";TrainerView.Render(c,model);Check(model.Search=="  flint  "&&model.Category==2,"clicking current navigation preserves search and category");}
        model.Search="";model.Category=0;model.ResourceScroll=300;
        using(PreviewCanvas c=new PreviewCanvas()){c.ClickId="nav.1";TrainerView.Render(c,model);Check(model.ResourceScroll==300,"clicking current navigation preserves valid scroll position");}
        model.Search="flint";
        using(PreviewCanvas c=new PreviewCanvas()){c.ClickId="search.clear";TrainerView.Render(c,model);Check(model.Search==""&&model.ResourceScroll==0,"clearing search resets its scroll position");}
        model.Search="flint";model.Navigate(2);model.Search="deer";model.Navigate(1);
        Check(model.Search=="flint","each catalog keeps its own search when navigating");
        model.Search="";model.ResourceScroll=0;
        using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,model);Check(c.Hits.Count(h=>h.Id.StartsWith("item."))==16&&c.Hits.Where(h=>h.Id.StartsWith("item.")).All(h=>h.Rect.H==112),"four complete catalog rows are visible without cropped names");}
        model.Page=2;model.SelectionCount=0;model.Location=0;
        using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,model);Check(c.Hits.Any(h=>h.Id=="location.target")&&c.Hits.Any(h=>h.Id=="location.center"),"animal generation exposes both location choices");Check(c.Texts.Any(t=>t.Text=="当前将使用画面中心"),"missing selection explains the actual spawn location");}
        model.Page=1;model.Busy=true;model.TaskName="原任务";model.SelectedResource="flint_fixture_0";model.Done=4;model.Total=10;
        using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,model);Check(c.Texts.Any(t=>t.Text=="正在添加 原任务 · 4/10"),"progress remains attached to original job after selection changes");}
        model.Busy=false;model.ShowSuccess=true;model.CompletedName="生肉";model.CompletedCount=10;
        using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,model);Check(c.Texts.Any(t=>t.Text=="已添加 生肉 × 10"),"success feedback names the completed item and amount");}
        model.SelectedResource="raw_meat";model.ShowSuccess=false;model.Page=0;
        using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,model);Check(c.Hits.First(h=>h.Id=="lock.0").Rect.W==244,"attribute row has a full-width click target");}
        using(PreviewCanvas c=new PreviewCanvas()){c.ClickId="targets.open";TrainerView.Render(c,model);Check(model.Page==5&&!c.Hits.Any(h=>h.Id=="kill"||h.Id=="complete"),"overview summarizes targets and opens the page that holds every target action");}
        bool shortcuts=true;
        for(int tab=0;tab<2;tab++){model.TargetTab=tab;using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,model);shortcuts&=c.Texts.Any(t=>t.Text==(tab==0?"启用 Del 快捷键":"启用 Ins 快捷键"));}}
        Check(shortcuts,"target switches explicitly enable shortcuts");model.TargetTab=0;
        model.Page=1;
        using(PreviewCanvas c=new PreviewCanvas()){c.FocusId="search";TrainerView.Render(c,model);Check(c.Focused("search")&&!c.Focused("quantity"),"focus states distinguish search and quantity fields");}
        using(PreviewCanvas c=new PreviewCanvas())
        {
            TrainerView.Render(c,model);UiHit add=c.Hits.First(h=>h.Id=="pack.add"),spawn=c.Hits.First(h=>h.Id=="spawn");
            Check(c.Texts.Any(t=>t.Text=="加入物资包「建村材料」"),"catalog names the pack that receives the item");
            Check(spawn.Rect.Y-(add.Rect.Y+add.Rect.H)>=8,"pack and primary buttons are clearly separated");
        }
    }
    private static bool Overlaps(List<UiHit> hits)
    {
        for(int a=0;a<hits.Count;a++)for(int b=a+1;b<hits.Count;b++)if(Intersects(hits[a].Rect,hits[b].Rect))return true;
        return false;
    }
    private static bool Intersects(UiRect a,UiRect b)
    {return Math.Min(a.X+a.W,b.X+b.W)-Math.Max(a.X,b.X)>1&&Math.Min(a.Y+a.H,b.Y+b.H)-Math.Max(a.Y,b.Y)>1;}
    private static int Main(string[] args)
    {
        try
        {
            string catalog=args.Length==0?"--fixtures":args[0];
            UiModel model=Model(catalog);GeometryChecks(model);FeatureUiChecks(Model(catalog));PackQuantityChecks(Model(catalog));MultiplierUiChecks(Model(catalog));
            if(args.Length<2)return Result();
            string output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
            string[] names={"01-基础功能","02-添加资源","03-产生动物","04-世界控制","05-使用说明"};
            for(int i=0;i<5;i++){UiModel m=Model(catalog);m.Page=i;using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,m);c.Save(Path.Combine(output,names[i]+".png"));}}
            UiModel empty=Model(catalog);empty.Page=1;empty.Search="找不到的物品";
            using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,empty);c.Save(Path.Combine(output,"06-搜索空状态.png"));}
            string[] stateNames={"07-资源数量错误","08-资源生成进度","09-动物数量错误","10-动物生成进度"};
            for(int i=0;i<4;i++)
            {
                UiModel m=Model(catalog);m.Page=1+i/2;m.Busy=i%2==1;m.Quantity=m.AnimalQuantity=m.Busy?"10":"0";m.Done=4;m.Total=10;m.TaskName=m.Page==2?"鹿":"生肉";
                using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,m);c.Save(Path.Combine(output,stateNames[i]+".png"));}
            }
            UiModel success=Model(catalog);success.Page=1;success.Quantity="100";success.ShowSuccess=true;success.CompletedName="生肉";success.CompletedCount=100;
            using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,success);c.Save(Path.Combine(output,"11-添加完成.png"));}
            UiModel hidden=Model(catalog);hidden.Page=1;hidden.Search="flint";
            using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,hidden);c.Save(Path.Combine(output,"12-筛选外已选物品.png"));}
            UiModel focus=Model(catalog);focus.Page=1;
            using(PreviewCanvas c=new PreviewCanvas()){c.FocusId="search";TrainerView.Render(c,focus);c.Save(Path.Combine(output,"13-搜索焦点.png"));c.SaveAtResolution(Path.Combine(output,"14-资源页-1280x720.png"),1280,720);c.SaveAtResolution(Path.Combine(output,"15-资源页-1366x768.png"),1366,768);}
            string[] featureNames={"16-选中单位救助","17-建造与维护","18-常用物资包","19-偏好设置","20-空选择救助","21-资源收藏","22-物资包数量错误","23-世界已有访客","24-慢速与面板暂停"};
            for(int i=0;i<featureNames.Length;i++)
            {
                UiModel m=Model(catalog);
                if(i==0||i==1||i==4){m.Page=5;m.TargetTab=i==1?1:0;if(i==4){m.SelectionCount=m.RescueTargets=m.TameTargets=0;for(int n=0;n<7;n++)m.TargetStats[n]=-1;}}
                if(i==2||i==6){m.Page=6;if(i==6)m.Preferences.Packs[0].Lines[0].Quantity="0";}
                if(i==3){m.Page=7;m.Preferences.AutoPause=true;}
                if(i==5){m.Page=1;m.Category=5;m.Preferences.Favorites.Add("flint");m.Preferences.Favorites.Add("log");m.Preferences.Favorites.Add("raw_meat");}
                if(i==7){m.Page=3;m.MigrantCount=3;m.TraderCount=2;}
                if(i==8){m.Page=3;m.Speed=.25f;m.AutoPauseActive=true;}
                using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,m);c.Save(Path.Combine(output,featureNames[i]+".png"));}
            }
            UiModel multipliers=Model(catalog);multipliers.Page=8;multipliers.StorageMultiplier=5;multipliers.BuildMultiplier=10;multipliers.CarryMultiplier=2;multipliers.TransportMultiplier=5;
            using(PreviewCanvas c=new PreviewCanvas(TrainerView.PanelHeight(8)))
            {
                TrainerView.Render(c,multipliers);c.Save(Path.Combine(output,"25-效率倍率.png"));
                c.SaveAtResolution(Path.Combine(output,"26-效率倍率-1280x720.png"),1280,720);
                c.SaveAtResolution(Path.Combine(output,"27-效率倍率-1366x768.png"),1366,768);
            }
        }
        catch(Exception ex){failed++;Console.WriteLine("FAIL "+ex);}
        return Result();
    }
    private static int Result(){Console.WriteLine("UI RESULT passed="+passed+" failed="+failed+"; shared layout rendered offline, game not launched");return failed==0?0:1;}
    private static void FeatureUiChecks(UiModel model)
    {
        List<string> actions=new List<string>();model.Action=(a,v)=>actions.Add(a+":"+v);
        model.Page=5;
        using(PreviewCanvas c=new PreviewCanvas()){c.ClickId="rescue.health";TrainerView.Render(c,model);Check(actions.SequenceEqual(new[]{"rescue.health:"}),"rescue button dispatches a one-time selection action");}
        model.RescueTargets=0;model.TameTargets=0;
        using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,model);Check(c.Hits.Where(h=>h.Id.StartsWith("rescue.")||h.Id=="tame").All(h=>!h.Enabled),"empty rescue and capture selections disable action buttons");}
        model.TargetTab=1;actions.Clear();
        using(PreviewCanvas c=new PreviewCanvas()){c.ClickId="supply.building";TrainerView.Render(c,model);Check(actions.Contains("supply.building:"),"maintenance view dispatches live building supply calculation");Check(c.Hits.All(h=>h.Rect.Y+h.Rect.H<=TrainerView.Height),"maintenance controls remain inside window");}
        model.Page=1;model.Quantity="10";actions.Clear();
        using(PreviewCanvas c=new PreviewCanvas()){c.ClickId="favorite";TrainerView.Render(c,model);Check(model.Preferences.Favorites.Contains("raw_meat"),"favorite button updates the actual saved preference model");}
        model.Category=5;
        using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,model);Check(c.Hits.Count(h=>h.Id.StartsWith("item."))==1,"favorites filter displays only saved favorites");}
        int before=model.Preferences.Packs[0].Lines.Count;
        using(PreviewCanvas c=new PreviewCanvas()){c.ClickId="pack.add";TrainerView.Render(c,model);Check(model.Preferences.Packs[0].Lines.Count==before+1,"selected resource and quantity can be added to a pack");}
        model.Page=6;actions.Clear();
        using(PreviewCanvas c=new PreviewCanvas()){c.ClickId="pack.execute";TrainerView.Render(c,model);Check(actions.Contains("pack.execute:"),"valid pack primary button dispatches batch creation");}
        model.Preferences.Packs[0].Lines[0].Quantity="0";
        using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,model);Check(c.Hits.Any(h=>h.Id=="pack.execute"&&!h.Enabled),"one invalid item disables the entire pack execution");}
        int packs=model.Preferences.Packs.Count;actions.Clear();
        using(PreviewCanvas c=new PreviewCanvas()){c.ClickId="pack.delete";TrainerView.Render(c,model);Check(model.Preferences.Packs.Count==packs&&actions.Contains("confirm.arm:pack.delete"),"deleting a pack waits for a second click");}
        using(PreviewCanvas c=new PreviewCanvas()){c.ClickId="pack.delete";TrainerView.Render(c,model);Check(model.Preferences.Packs.Count==packs-1,"second click deletes the pack");}
        model.PackSearch="flint";int lines=model.Preferences.Packs[0].Lines.Count;
        using(PreviewCanvas c=new PreviewCanvas()){c.ClickId="pack.pick.flint";TrainerView.Render(c,model);Check(c.Hits.Count(h=>h.Id.StartsWith("pack.pick."))==3&&model.Preferences.Packs[0].Lines.Count==lines+1&&model.Preferences.Packs[0].Lines.Any(l=>l.Id=="flint"&&l.Quantity=="10"),"pack editor searches the catalog and adds items in place");}
        model.PackSearch="";model.Preferences.Packs[0].Lines[0].Quantity="0";
        using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,model);Check(c.Texts.Any(t=>t.Text=="每项数量需要为 1–1000")&&c.Texts.Any(t=>t.Text=="请先修正清单"),"invalid pack names the problem and the button asks for a fix");}
        model.Preferences.Packs[0].Lines[0].Quantity="30";
        model.Page=3;model.HousingFree=0;model.TraderCount=1;
        using(PreviewCanvas c=new PreviewCanvas()){TrainerView.Render(c,model);Check(c.Hits.Any(h=>h.Id=="invite.migrant"&&!h.Enabled)&&c.Hits.Any(h=>h.Id=="invite.trader"&&!h.Enabled),"housing and existing visitors disable duplicate invitations");}
        actions.Clear();
        using(PreviewCanvas c=new PreviewCanvas()){c.ClickId="speed.0.25";TrainerView.Render(c,model);Check(actions.Contains("speed:0.25"),"quarter-speed preset emits exact fractional speed");}
        model.Page=7;actions.Clear();
        using(PreviewCanvas c=new PreviewCanvas()){c.ClickId="key.capture.0";TrainerView.Render(c,model);Check(actions.Contains("key.capture:0"),"shortcut button starts capture for the intended action");}
        using(PreviewCanvas c=new PreviewCanvas()){c.ClickId="prefs.autopause";bool old=model.Preferences.AutoPause;TrainerView.Render(c,model);Check(model.Preferences.AutoPause!=old,"automatic pause preference is directly editable");}
        using(PreviewCanvas c=new PreviewCanvas()){c.ClickId="ui.scale.125";TrainerView.Render(c,model);Check(model.Preferences.UiScale==125,"interface size preference is directly editable");}
        model.Busy=true;actions.Clear();
        using(PreviewCanvas c=new PreviewCanvas()){c.ClickId="spawn.cancel";TrainerView.Render(c,model);Check(actions.Contains("spawn.cancel:"),"active generation exposes an explicit stop action");}
    }
    private static void PackQuantityChecks(UiModel model)
    {
        model.Preferences.Packs.Clear();SupplyPack pack=new SupplyPack();model.Preferences.Packs.Add(pack);
        SupplyLine line=new SupplyLine{Id="raw_meat"};pack.Lines.Add(line);
        model.Page=6;model.PackSearch="raw_meat";
        string[] invalidExisting={"-10","0","abc","",Int32.MinValue.ToString()};
        foreach(string quantity in invalidExisting)
        {
            line.Quantity=quantity;
            using(PreviewCanvas c=new PreviewCanvas())
            {
                c.ClickId="pack.pick.raw_meat";TrainerView.Render(c,model);
                Check(line.Quantity=="10"&&pack.Lines.Count==1,"adding an existing pack item repairs quantity '"+quantity+"' without a duplicate row");
                Check(c.Hits.Any(h=>h.Id=="pack.execute"&&h.Enabled),"repaired pack quantity '"+quantity+"' allows execution");
            }
        }
        using(PreviewCanvas c=new PreviewCanvas()){c.ClickId="pack.pick.raw_meat";TrainerView.Render(c,model);Check(line.Quantity=="20","adding a repaired pack item continues from its repaired amount");}

        // The catalog also adds to existing rows; huge saved values must be
        // rejected before integer addition, without the pack input truncating them.
        model.Page=1;model.SelectedResource="raw_meat";model.Quantity="10";
        List<string> notices=new List<string>();model.Action=(a,v)=>{if(a=="pack.notice")notices.Add(v);};
        string[] overLimit={"1000","1001",Int32.MaxValue.ToString()};
        foreach(string quantity in overLimit)
        {
            line.Quantity=quantity;notices.Clear();
            using(PreviewCanvas c=new PreviewCanvas())
            {
                c.ClickId="pack.add";TrainerView.Render(c,model);
                Check(line.Quantity==quantity&&pack.Lines.Count==1&&notices.SequenceEqual(new[]{"该物资包中的单项数量最多 1000"}),"existing quantity '"+quantity+"' rejects addition without overflow or losing its value");
            }
        }
        line.Quantity="990";notices.Clear();
        using(PreviewCanvas c=new PreviewCanvas()){c.ClickId="pack.add";TrainerView.Render(c,model);Check(line.Quantity=="1000"&&notices.Count==1&&!notices[0].Contains("最多"),"adding exactly to the 1000 item boundary succeeds");}

        string[] formatted={" 10 ","0010","+10"};
        foreach(string quantity in formatted)
        {
            pack.Lines.Clear();model.Quantity=quantity;
            using(PreviewCanvas c=new PreviewCanvas()){c.ClickId="pack.add";TrainerView.Render(c,model);Check(pack.Lines.Count==1&&pack.Lines[0].Quantity=="10","new catalog item normalizes quantity '"+quantity+"'");}
        }
        string[] invalidNew={"-1","0","abc","","1001"};
        foreach(string quantity in invalidNew)
        {
            pack.Lines.Clear();model.Quantity=quantity;notices.Clear();
            using(PreviewCanvas c=new PreviewCanvas())
            {
                c.ClickId="pack.add";TrainerView.Render(c,model);
                Check(pack.Lines.Count==0&&notices.Count==0&&c.Hits.Any(h=>h.Id=="pack.add"&&!h.Enabled),"new catalog quantity '"+quantity+"' cannot insert an invalid pack row");
            }
        }
    }
    private static void MultiplierUiChecks(UiModel model)
    {
        Check(model.Version=="1.4.1"&&model.StorageMultiplier==1&&model.BuildMultiplier==1&&model.CarryMultiplier==1&&model.TransportMultiplier==1,"four integer multipliers start at 1x in version 1.4.1");
        model.Page=8;
        string[] keys={"storage","build","carry","transport"};int[] maximums={10,20,10,10};
        Action<int>[] set={n=>model.StorageMultiplier=n,n=>model.BuildMultiplier=n,n=>model.CarryMultiplier=n,n=>model.TransportMultiplier=n};
        Func<int>[] get={()=>model.StorageMultiplier,()=>model.BuildMultiplier,()=>model.CarryMultiplier,()=>model.TransportMultiplier};
        List<string> actions=new List<string>();model.Action=(a,v)=>actions.Add(a+":"+v);
        using(PreviewCanvas c=new PreviewCanvas(TrainerView.PanelHeight(8)))
        {
            TrainerView.Render(c,model);
            Check(c.Hits.Where(h=>h.Id.StartsWith("nav.")).Select(h=>h.Id).SequenceEqual(new[]{"nav.0","nav.5","nav.1","nav.2","nav.6","nav.3","nav.8","nav.7","nav.4"}),"efficiency page occupies the intended sidebar position");
            Check(c.Texts.Count(t=>t.Text=="当前 1×")==4,"each multiplier displays its current integer value");
            Check(c.Texts.Any(t=>t.Text.Contains("不含露天堆场"))&&c.Texts.Any(t=>t.Text.Contains("材料与搬运照常"))&&c.Texts.Any(t=>t.Text.Contains("儿童、重物保持"))&&c.Texts.Any(t=>t.Text.Contains("仅拖板与拖车"))&&c.Texts.Any(t=>t.Text.Contains("保留现货")),"efficiency page explains each scope and retains stock when capacity decreases");
            Check(TrainerView.PanelHeight(8)==640&&Enumerable.Range(0,8).All(p=>TrainerView.PanelHeight(p)==800),"only the efficiency page uses the compact 640px panel height");
            Check(c.Hits.Where(h=>h.Id.Contains("multiplier.")).Count()==29&&c.Hits.Where(h=>h.Id.Contains("multiplier.")).All(h=>h.Rect.H>=32),"all four sliders and multiplier buttons retain at least 32px click height");
            Check(c.Hits.Where(h=>h.Id.StartsWith("slider.multiplier.")).All(h=>h.Rect.W==302)&&!Overlaps(c.Hits),"integer sliders share their visible track width and do not overlap neighboring controls");
            Check(c.Texts.Where(t=>t.Text.Contains("保留现货")||t.Text.Contains("关闭所有开关")||t.Text==model.Status).All(t=>t.Rect.Y+t.Rect.H<=640),"compact explanatory notes and status remain inside the panel");
            Check(c.Hits.Where(h=>h.Id.StartsWith("nav.")).All(h=>h.Rect.H==38&&h.Rect.Y+h.Rect.H<=512),"compact sidebar navigation ends above the connection card");
            Check(!c.Texts.Any(t=>t.Text=="常用倍率"),"compact cards omit the redundant preset heading");
        }
        for(int i=0;i<keys.Length;i++)
        {
            string id="multiplier."+keys[i];int index=i;
            set[index](1);actions.Clear();
            using(PreviewCanvas c=new PreviewCanvas()){c.ClickId=id+".minus";TrainerView.Render(c,model);Check(get[index]()==1&&actions.Count==0&&c.Hits.Any(h=>h.Id==id+".minus"&&!h.Enabled),keys[i]+" multiplier cannot go below one");}
            set[index](maximums[index]);actions.Clear();
            using(PreviewCanvas c=new PreviewCanvas()){c.ClickId=id+".plus";TrainerView.Render(c,model);Check(get[index]()==maximums[index]&&actions.Count==0&&c.Hits.Any(h=>h.Id==id+".plus"&&!h.Enabled),keys[i]+" multiplier cannot exceed its integer maximum");}
            set[index](7);actions.Clear();
            using(PreviewCanvas c=new PreviewCanvas()){c.ClickId=id+".plus";TrainerView.Render(c,model);Check(get[index]()==8&&actions.SequenceEqual(new[]{id+":8"}),keys[i]+" plus increments by exactly one and emits an integer");}
            actions.Clear();
            using(PreviewCanvas c=new PreviewCanvas()){c.ClickId=id+".minus";TrainerView.Render(c,model);Check(get[index]()==7&&actions.SequenceEqual(new[]{id+":7"}),keys[i]+" minus decrements by exactly one and emits an integer");}
            int[] presets=maximums[index]==20?new[]{1,2,5,10,20}:new[]{1,2,5,10};
            foreach(int preset in presets)
            {
                set[index](preset==1?2:1);actions.Clear();
                using(PreviewCanvas c=new PreviewCanvas()){c.ClickId=id+"."+preset;TrainerView.Render(c,model);Check(get[index]()==preset&&actions.SequenceEqual(new[]{id+":"+preset}),keys[i]+" preset "+preset+" dispatches its own integer setting");}
            }
            actions.Clear();
            using(PreviewCanvas c=new PreviewCanvas()){c.ClickId=id+"."+get[index]();TrainerView.Render(c,model);Check(actions.Count==0,keys[i]+" unchanged preset does not dispatch a duplicate setting");}
            float[] rawValues={4.49f,4.5f,-10,maximums[index]+10,Single.MaxValue};int[] expected={4,5,1,maximums[index],maximums[index]};
            for(int sample=0;sample<rawValues.Length;sample++)
            {
                set[index](7);actions.Clear();
                using(PreviewCanvas c=new PreviewCanvas(640))
                {
                    c.NextSliders[id+".slider"]=rawValues[sample];TrainerView.Render(c,model);
                    Check(get[index]()==expected[sample]&&actions.SequenceEqual(new[]{id+":"+expected[sample]})&&c.Texts.Any(t=>t.Text=="当前 "+expected[sample]+"×"),keys[i]+" slider rounds or clamps input "+rawValues[sample].ToString(CultureInfo.InvariantCulture)+" to one integer setting");
                }
            }
            float[] unchangedValues={7,7.49f,Single.NaN,Single.PositiveInfinity,Single.NegativeInfinity};
            foreach(float raw in unchangedValues)
            {
                set[index](7);actions.Clear();
                using(PreviewCanvas c=new PreviewCanvas(640))
                {
                    c.NextSliders[id+".slider"]=raw;TrainerView.Render(c,model);
                    Check(get[index]()==7&&actions.Count==0,keys[i]+" slider ignores unchanged or nonfinite input "+raw.ToString(CultureInfo.InvariantCulture));
                }
            }
        }
        model.Ready=false;
        for(int i=0;i<keys.Length;i++)
        {
            set[i](5);actions.Clear();
            using(PreviewCanvas c=new PreviewCanvas(640))
            {
                c.ClickId="multiplier."+keys[i]+".plus";c.NextSliders["multiplier."+keys[i]+".slider"]=9.5f;TrainerView.Render(c,model);
                Check(get[i]()==5&&actions.Count==0&&c.Hits.Where(h=>h.Id.Contains("multiplier.")).Count()==29&&c.Hits.Where(h=>h.Id.Contains("multiplier.")).All(h=>!h.Enabled)&&c.SliderCalls.Count==0,keys[i]+" and every multiplier control are disabled without a map");
            }
        }
    }
}
