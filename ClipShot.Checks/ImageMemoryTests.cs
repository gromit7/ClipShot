using ClipShot;
using Xunit;

namespace ClipShot.Checks;

public sealed class ImageMemoryTests
{
    [Fact]
    public void Metadata_preserves_groups_and_original_images_without_loading_payloads()
    {
        using var workspace=new Testing.TestWorkspace();
        using var store=new Store(Path.Combine(workspace.Root,"images.db"));
        store.Add("text","synthetic text","html","rtf");
        store.Add("image","",image:[1,2,3,4]);
        var original=store.All().First(c=>c.Kind=="image");
        store.AddEditedImage(original.Id,[5,6,7,8]);
        store.Template(original.Id,true);store.Edit(original.Id,"synthetic template");
        var full=store.All();var metadata=store.All(includeImages:false);
        Assert.Equal(full.Select(c=>c with{Image=null}),metadata);
        Assert.All(metadata,c=>Assert.Null(c.Image));
        foreach(var clip in full)Assert.Equal(clip.Image,store.ReadImage(clip.Id));
        Assert.Null(store.ReadImage(long.MaxValue));
        Assert.Equal(HistoryView.Groups(full,"","",DateTime.Today).Select(g=>(g.Title,string.Join(',',g.Clips.Select(c=>c.Id)))),
            HistoryView.Groups(metadata,"","",DateTime.Today).Select(g=>(g.Title,string.Join(',',g.Clips.Select(c=>c.Id)))));
        store.Delete(original.Id);Assert.Null(store.ReadImage(original.Id));
    }

    [Fact]
    public void History_listing_does_not_allocate_image_history_size()
    {
        using var workspace=new Testing.TestWorkspace();
        using var store=new Store(Path.Combine(workspace.Root,"memory.db"));
        byte[] payload=new byte[1024*1024];
        for(int i=0;i<64;i++){payload[0]=(byte)i;store.Add("image","",image:payload);}
        // Warm up both query paths before measuring synchronous SQLite allocations.
        store.All(includeImages:false);store.All();
        long start=GC.GetAllocatedBytesForCurrentThread();
        var metadata=store.All(includeImages:false);
        long metadataBytes=GC.GetAllocatedBytesForCurrentThread()-start;
        start=GC.GetAllocatedBytesForCurrentThread();
        var full=store.All();
        long fullBytes=GC.GetAllocatedBytesForCurrentThread()-start;
        TestContext.Current.TestOutputHelper!.WriteLine($"IMAGE_MEMORY: rows={metadata.Count}; payloadBytes={full.Sum(c=>(long)c.Image!.Length)}; fullAllocated={fullBytes}; metadataAllocated={metadataBytes}");
        Assert.Equal(64,metadata.Count);Assert.All(metadata,c=>Assert.Null(c.Image));
        Assert.True(fullBytes>=64L*1024*1024);
        Assert.True(metadataBytes<1024*1024,$"Metadata allocated {metadataBytes} bytes");
        GC.KeepAlive(full);GC.KeepAlive(metadata);
    }
}
