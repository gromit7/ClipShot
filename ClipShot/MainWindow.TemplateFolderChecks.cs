using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Security.Cryptography;
using System.Text.Json;

namespace ClipShot;
public sealed partial class MainWindow
{
    private async Task RunTemplateFolderChecks(List<string> results)
    {
        if(!isolated)throw new InvalidOperationException("Template folder checks require isolated data.");
        void Check(bool ok,string name)=>results.Add($"{(ok?"PASS":"FAIL")} {name}");
        async Task Settle(){await Task.Delay(80);Root.UpdateLayout();PositionSection();await Task.Delay(40);Root.UpdateLayout();}
        string ClipsSnapshot()=>JsonSerializer.Serialize(store.All().Select(clip=>new{clip.Id,clip.Kind,clip.Text,clip.Html,clip.Rtf,Image=clip.Image is null?null:Convert.ToHexString(SHA256.HashData(clip.Image)),clip.Name,clip.Template,clip.Seen}));
        var originalIds=store.All().Select(clip=>clip.Id).ToHashSet();var originalFolders=store.GetTemplateLayout().Folders.Select(folder=>folder.Id).ToHashSet();
        var originalExpanded=expandedTemplateFolders.ToDictionary(pair=>pair.Key,pair=>pair.Value);
        string layout=store.Get("sectionlayout","side"),query=Search.Text,originalOrder=store.Get("templateorder","");bool templatesExpanded=expanded.GetValueOrDefault("Шаблоны");
        string? section=activeSection;bool initialized=sectionInitialized;long suppression=templateTapSuppressedUntil;
        try
        {
            foreach(string text in new[]{"synthetic folder A","synthetic folder B","synthetic folder C"}){store.Add("text",text);store.Template(store.All().Single(clip=>clip.Text==text).Id,true);}
            long a=store.All().Single(clip=>clip.Text=="synthetic folder A").Id,b=store.All().Single(clip=>clip.Text=="synthetic folder B").Id,c=store.All().Single(clip=>clip.Text=="synthetic folder C").Id;
            string keyA=TemplateClipKey(a),keyB=TemplateClipKey(b),keyC=TemplateClipKey(c);
            Search.Text="";store.Set("sectionlayout","inline");expanded["Шаблоны"]=true;Render();await Settle();string snapshot=ClipsSnapshot();
            var initial=visible.Single(item=>item.Clip.Id==a).Row;
            Check(IsTemplateDragRow(initial)&&!initial.CanDrag&&!initial.AllowDrop&&!BeginTemplatePointerDrag(new Border(),keyA,new()),"template dragging is internal and cannot start from an unregistered external row");
            Check(!BeginTemplatePointerDrag(initial,"c:-1",new())&&!BeginTemplatePointerDrag(initial,keyB,new()),"template pointer drag requires the registered canonical metadata key");

            await ApplyTemplateDrop(keyC,keyA,TemplateDropZone.Before);
            var order=store.GetTemplateLayout().RootOrder;Check(order.ToList().IndexOf(keyC)+1==order.ToList().IndexOf(keyA),"dropping on the upper strip reorders independent template entries");
            string beforeCancel=JsonSerializer.Serialize(store.GetTemplateLayout());
            bool cancelled=await ApplyTemplateDrop(keyA,keyB,TemplateDropZone.Inside,()=>Task.FromResult<string?>(null));
            Check(!cancelled&&beforeCancel==JsonSerializer.Serialize(store.GetTemplateLayout())&&snapshot==ClipsSnapshot(),"cancelling the folder name leaves order and all clip contents unchanged");
            await Settle();
            var source=visible.Single(item=>item.Clip.Id==a).Row;var target=visible.Single(item=>item.Clip.Id==b).Row;
            bool sourceArea=TryTemplatePointerArea(source,out _,out var sourceBounds),targetArea=TryTemplatePointerArea(target,out _,out var targetBounds);
            Native.Point Center(SectionScreenArea area)=>new(){X=(int)((area.Left+area.Right)/2),Y=(int)((area.Top+area.Bottom)/2)};
            var start=Center(sourceBounds);var finish=Center(targetBounds);templateTapSuppressedUntil=0;
            Check(sourceArea&&targetArea&&BeginTemplatePointerDrag(source,keyA,start),"a registered visible template starts an isolated mouse gesture without changing the system cursor");
            await UpdateTemplatePointerDrag(new Native.Point{X=start.X+2,Y=start.Y+2},true);
            Check(!templateDragInProgress&&!SuppressTemplateTap(),"movement below six physical pixels preserves ordinary click behavior");
            await UpdateTemplatePointerDrag(start,false);
            string beforeCoalesced=JsonSerializer.Serialize(store.GetTemplateLayout());
            BeginTemplatePointerDrag(source,keyA,start);
            bool askedCoalesced=false;
            bool coalesced=await UpdateTemplatePointerDrag(finish,false,()=>{askedCoalesced=true;return Task.FromResult<string?>(null);});
            Check(askedCoalesced&&!coalesced&&!templateDragInProgress&&SuppressTemplateTap()&&beforeCoalesced==JsonSerializer.Serialize(store.GetTemplateLayout()),"a release containing coalesced movement reaches the folder prompt without needing a moved event or hardware button state");
            await Settle();source=visible.Single(item=>item.Clip.Id==a).Row;target=visible.Single(item=>item.Clip.Id==b).Row;
            string beforeOutside=JsonSerializer.Serialize(store.GetTemplateLayout());
            BeginTemplatePointerDrag(source,keyA,start);
            var outside=new Native.Point{X=-100000,Y=-100000};await UpdateTemplatePointerDrag(outside,true);
            bool outsideDrop=await UpdateTemplatePointerDrag(outside,false);
            Check(!outsideDrop&&!templateDragInProgress&&templatePointerRow is null&&beforeOutside==JsonSerializer.Serialize(store.GetTemplateLayout())&&snapshot==ClipsSnapshot(),"releasing outside every visible target cancels the pointer gesture without changing templates");
            await Settle();source=visible.Single(item=>item.Clip.Id==a).Row;target=visible.Single(item=>item.Clip.Id==b).Row;
            BeginTemplatePointerDrag(source,keyA,start);await UpdateTemplatePointerDrag(finish,true);
            Check(templateDragInProgress&&templateDropIndicator?.Visibility==Visibility.Visible&&templateDraggedEntry==keyA,"the shared pointer path crosses its threshold and displays a center drop marker");
            bool pointerCreated=await UpdateTemplatePointerDrag(finish,false,()=>Task.FromResult<string?>("IP-адреса"));await Settle();
            var folder=store.GetTemplateLayout().Folders.Single(item=>item.Name=="IP-адреса");
            Check(pointerCreated&&!templateDragInProgress&&templatePointerRow is null&&SuppressTemplateTap()&&folder.ClipIds.SequenceEqual(new[]{b,a})&&templateFolderButtons.ContainsKey(folder.Id),"releasing through the shared pointer path creates a named folder and suppresses accidental copying");
            store.Set("sectionlayout","side");activeSection="Шаблоны";sectionInitialized=true;Render();await Settle();
            Check(templateFolderButtons.ContainsKey(folder.Id)&&visible.Where(item=>item.Clip.Id==a||item.Clip.Id==b).Count()==2&&visible.Where(item=>item.Clip.Id==a||item.Clip.Id==b).All(item=>IsTemplateDragRow(item.Row)&&!item.Row.CanDrag),"default side popup renders folders and keeps expanded template children internally draggable without treating force-open as search");
            source=visible.Single(item=>item.Clip.Id==a).Row;target=visible.Single(item=>item.Clip.Id==b).Row;
            bool sideSource=TryTemplatePointerArea(source,out _,out sourceBounds),sideTarget=TryTemplatePointerArea(target,out _,out targetBounds);
            BeginTemplatePointerDrag(source,keyA,Center(sourceBounds));await UpdateTemplatePointerDrag(Center(targetBounds),true);
            Check(sideSource&&sideTarget&&FindTemplatePointerTarget(Center(targetBounds))?.Entry.Key==keyB,"side popup pointer hit testing uses its own screen origin and visible scroll viewport");
            EndTemplateDrag();
            store.Set("sectionlayout","inline");Render();await Settle();
            Check(IsTemplateFolderKeySource(templateFolderButtons[folder.Id])&&templateFolderButtons[folder.Id].Content is Grid,"folder header has a keyboard source guard so Enter and Space cannot copy a selected clip");
            ToggleTemplateFolder(folder.Id);await Settle();
            Check(visible.All(item=>item.Clip.Id!=a&&item.Clip.Id!=b)&&Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(templateFolderHeaderRows[folder.Id]) is not null,"a collapsed folder hides its child rows while its header remains inside the visible template list");
            ToggleTemplateFolder(folder.Id);await Settle();
            await ApplyTemplateDrop(keyC,keyA,TemplateDropZone.Inside);await Settle();
            Check(store.GetTemplateLayout().Folders.Single(item=>item.Id==folder.Id).ClipIds.SequenceEqual(new[]{b,a,c}),"dropping onto a child joins its existing folder without nesting");
            MoveTemplate(c,-1);
            Check(store.GetTemplateLayout().Folders.Single(item=>item.Id==folder.Id).ClipIds.SequenceEqual(new[]{b,c,a}),"existing move-up actions reorder within the current folder");
            Check(!await ApplyTemplateDrop(TemplateFolderKey(folder.Id),keyA,TemplateDropZone.Inside),"dragging a folder into a child cannot create nesting");

            OpenSearchClick(this,new());Search.Text="synthetic folder";await Settle();
            int matched=visible.Count(item=>item.Clip.Template);bool dragging=visible.Where(item=>item.Clip.Template).Any(item=>IsTemplateDragRow(item.Row)||item.Row.CanDrag);
            Check(Search.Text=="synthetic folder"&&Search.Visibility==Visibility.Visible&&templateFolderButtons.Count==0&&matched==3&&!dragging,
                $"search flattens matched template clips and disables dragging (query={Search.Text}; visibility={Search.Visibility}; folders={templateFolderButtons.Count}; matched={matched}; canDrag={dragging})");
            Search.Text="";Render();await Settle();
            templateDragInProgress=true;
            Check(UpdateOperationBusy&&!CanDismissPanel&&SuppressTemplateTap(),"an active template drag protects automatic installation, panel dismissal and accidental copying");
            var sourceRow=visible.First(item=>item.Clip.Id==a).Row;Render();
            Check(templateRenderPending&&ReferenceEquals(sourceRow,visible.First(item=>item.Clip.Id==a).Row),"clipboard or theme redraw during an internal drag is deferred without replacing its source row");
            EndTemplateDrag();Check(!templateDragInProgress&&SuppressTemplateTap(),"a short post-drag tap guard prevents a completed drag from copying a clip");await Settle();
            Check(!templateRenderPending&&!ReferenceEquals(sourceRow,visible.First(item=>item.Clip.Id==a).Row),"ending or cancelling a drag applies a pending redraw even without a drop handler");
            templateTapSuppressedUntil=0;UngroupTemplateFolder(folder.Id);await Settle();
            Check(!store.GetTemplateLayout().Folders.Any(item=>item.Id==folder.Id)&&snapshot==ClipsSnapshot(),"ungrouping restores root entries without changing text, formats, timestamps or clipboard data");
        }
        finally
        {
            EndTemplateDrag();templateTapSuppressedUntil=suppression;
            foreach(var folder in store.GetTemplateLayout().Folders.Where(folder=>!originalFolders.Contains(folder.Id)).ToArray())store.UngroupTemplateFolder(folder.Id);
            foreach(var clip in store.All())if(!originalIds.Contains(clip.Id))store.Delete(clip.Id);
            expandedTemplateFolders.Clear();foreach(var pair in originalExpanded)expandedTemplateFolders[pair.Key]=pair.Value;
            store.SetMany(new Dictionary<string,string>{{"sectionlayout",layout},{"templateorder",originalOrder}});Search.Text=query;expanded["Шаблоны"]=templatesExpanded;activeSection=section;sectionInitialized=initialized;Render();await Settle();
        }
    }
}
