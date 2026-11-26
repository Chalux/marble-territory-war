using Godot;
using System;

namespace TerritoryWar;

// Records ownership changes, independently of projectile trails or particle settings.
public sealed class VisualPaintAge : IDisposable
{
    public const float Unpainted=-100000;
    // Each cell can light up only its current owner. Store its paint time and faction index.
    private readonly float[] _samples=new float[TerritoryGrid.Width*TerritoryGrid.Height*2];
    private readonly long[] _versions=new long[TerritoryGrid.Width*TerritoryGrid.Height];
    private readonly byte[] _bytes=new byte[TerritoryGrid.Width*TerritoryGrid.Height*8];
    private readonly Image _image;
    public ImageTexture Texture {get;}
    private TerritoryGrid _grid;
    private long _revision=-1;
    private double _time=-1;
    private bool _dirty;
    public VisualPaintAge()
    {
        ClearSamples();Buffer.BlockCopy(_samples,0,_bytes,0,_bytes.Length);
        _image=Image.CreateFromData(TerritoryGrid.Width,TerritoryGrid.Height,false,Image.Format.Rgf,_bytes);
        Texture=ImageTexture.CreateFromImage(_image);
    }
    private void ClearSamples()
    {
        for(int i=0;i<_samples.Length;i+=2) _samples[i]=Unpainted;
    }
    public float PaintedAt(int cell,int faction)=>_samples[cell*2+1]==faction?_samples[cell*2]:Unpainted;
    public void Observe(TerritoryGrid grid,double time)
    {
        if(grid!=_grid||time<_time||grid.Revision<_revision)
        {
            ClearSamples();Array.Clear(_versions);_revision=-1;_dirty=true;
        }
        _grid=grid;_time=time;
        if(grid.Revision==_revision)return;
        for(int cell=0;cell<_versions.Length;cell++)
        {
            long version=grid.ChangedAtRevision[cell];
            if(version==_versions[cell])continue;
            _versions[cell]=version;
            int owner=grid.Owners[cell];
            _samples[cell*2]=owner==0?Unpainted:(float)time;
            _samples[cell*2+1]=Math.Max(0,owner-1);
            _dirty=true;
        }
        _revision=grid.Revision;
    }
    public void Upload()
    {
        if(!_dirty)return;
        Buffer.BlockCopy(_samples,0,_bytes,0,_bytes.Length);
        _image.SetData(TerritoryGrid.Width,TerritoryGrid.Height,false,Image.Format.Rgf,_bytes);
        Texture.Update(_image);_dirty=false;
    }
    public void Dispose(){Texture.Dispose();_image.Dispose();}
}
