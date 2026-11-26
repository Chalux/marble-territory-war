shader_type canvas_item;
uniform sampler2D contour_field : filter_linear, repeat_disable;
uniform sampler2D coverage_field : filter_linear, repeat_disable;
uniform sampler2D paint_age_field : filter_nearest, repeat_disable;
uniform float paint_fade_seconds = 1.6;
uniform float simulation_time = 0.0;
uniform bool particles_enabled = true;
float hash(vec2 p) {
    vec3 q=fract(vec3(p.xyx)*0.1031);q+=dot(q,q.yzx+33.33);
    return fract((q.x+q.y)*q.z);
}
float noise(vec2 p) {
    vec2 i=floor(p),f=fract(p);f=f*f*(3.0-2.0*f);
    return mix(mix(hash(i),hash(i+vec2(1,0)),f.x),mix(hash(i+vec2(0,1)),hash(i+vec2(1,1)),f.x),f.y);
}
vec4 contour_distance(sampler2D field,vec2 uv) {
    vec2 size=vec2(textureSize(field,0));
    vec2 pixel=uv*size-0.5, base=floor(pixel), f=fract(pixel);
    // Interpolating cubic reconstruction, reduced to nine bilinear fetches.
    // Unlike bilinear colour coverage, this has a continuous tangent at cell joins.
    vec2 w0=f*(-0.5+f*(1.0-f*0.5));
    vec2 w1=1.0+f*f*(-2.5+f*1.5);
    vec2 w2=f*(0.5+f*(2.0-f*1.5));
    vec2 w3=f*f*(-0.5+f*0.5);
    vec2 middle=w1+w2;
    vec3 px=vec3(base.x-1.0,base.x+w2.x/middle.x,base.x+2.0)+0.5;
    vec3 py=vec3(base.y-1.0,base.y+w2.y/middle.y,base.y+2.0)+0.5;
    vec3 wx=vec3(w0.x,middle.x,w3.x), wy=vec3(w0.y,middle.y,w3.y);
    vec4 result=vec4(0);
    for(int y=0;y<3;y++)for(int x=0;x<3;x++)
        result+=texture(field,vec2(px[x],py[y])/size)*wx[x]*wy[y];
    return result*8.0-4.0;
}
float freshness(vec2 cell,vec2 size,vec4 weights) {
    vec2 paint=texture(paint_age_field,(cell+0.5)/size).rg;
    return weights[int(paint.y)]*exp(-max(0.0,simulation_time-paint.x)/max(paint_fade_seconds,0.01));
}
float paint_glow(vec2 uv,vec4 weights) {
    vec2 size=vec2(textureSize(paint_age_field,0));
    vec2 p=uv*size-0.5,cell=floor(p),f=fract(p);
    // Interpolate brightness, not timestamps: old/empty neighbors must not erase a fresh edge.
    return mix(mix(freshness(cell,size,weights),freshness(cell+vec2(1,0),size,weights),f.x),
        mix(freshness(cell+vec2(0,1),size,weights),freshness(cell+vec2(1,1),size,weights),f.x),f.y);
}
void fragment() {
    float coverage=texture(coverage_field,UV).r*8.0-4.0;
    if(abs(coverage)<2.0) coverage=contour_distance(coverage_field,UV).r;
    float aa=max(fwidth(coverage)*0.65,0.025);
    float alpha=1.0-smoothstep(-aa,aa,coverage);
    if(alpha<=0.0) {
        COLOR=vec4(0);
    } else {
    vec4 distance=contour_distance(contour_field,UV);
    float nearest=min(min(distance.r,distance.g),min(distance.b,distance.a));
    vec4 weights=1.0-smoothstep(vec4(0),vec4(0.3),distance-vec4(nearest));
    weights/=max(dot(weights,vec4(1)),0.000001);
    vec3 tint=weights.r*vec3(1.0,0.025,0.15)+weights.g*vec3(0.005,1.0,0.55)+weights.b*vec3(0.008,0.48,1.0)+weights.a*vec3(1.0,0.55,0.008);
    vec2 origin=weights.r*vec2(0.12963,0.11538)+weights.g*vec2(0.87037,0.11538)+weights.b*vec2(0.12963,0.88462)+weights.a*vec2(0.87037,0.88462);
    vec2 world=UV*vec2(1080,1040);
    vec2 d=(UV-origin)*vec2(1.03846,1);
    float radius=length(d),angle=atan(d.y,d.x),t=simulation_time;
    float fresh=clamp(paint_glow(UV,weights),0.0,1.0);
    float edge_band=1.0-smoothstep(0.45,3.2,abs(nearest));
    float edge=1.0-smoothstep(0.0,0.75,abs(nearest));
    // Soft layered pigment, following the reference's broad translucent folds.
    // Motion is strongest at the boundary and where territory has just changed hands.
    vec2 q=world*0.008;
    vec2 warp=vec2(noise(q+vec2(t*0.045,2.1)),noise(q+vec2(6.7,-t*0.037)));
    vec2 curl=vec2(noise(q*2.1+warp*2.3+vec2(1,t*0.05)),noise(q*2.1+warp*2.3-vec2(t*0.04,3)));
    float handed=weights.r-weights.g-weights.b+weights.a;
    float bend=angle+handed*1.35*log(1.0+radius*6.0);
    bend+=(warp.x-0.5)*0.75+sin(angle*3.0-radius*7.0+t*0.16)*0.12;
    vec2 winding=vec2(cos(bend),sin(bend));
    vec2 sheet=winding*(2.0+radius*4.0)+(curl-0.5)*1.8;
    float broad=noise(sheet*2.4);
    float folds=noise(sheet*vec2(4.0,8.5)+(warp-0.5)*2.0);
    float detail=noise(sheet*vec2(11.0,23.0)+(curl-0.5)*2.5);
    float pigment=0.55*broad+0.33*folds+0.12*detail;
    vec3 deep=tint*0.08;
    vec3 color=mix(deep,tint,0.20+noise(world*0.005)*0.09);
    float moving_band=max(edge_band,fresh);
    color+=tint*moving_band*(0.12+pigment*0.40);
    // Subtle irregular inward grain replaces the separated bright cord-like ribbons.
    float grain=noise(winding*34.0+vec2(radius*5.0));
    float inward=noise(vec2(radius*17.0+t*0.55,grain*5.0));
    color+=tint*pow(grain,3.0)*inward*0.16*smoothstep(0.03,0.12,radius);
    color+=mix(tint,vec3(1),0.12)*edge*(0.15+fresh*0.20);
    // Newly earned pigment blooms, then settles to the same mature colour everywhere.
    vec3 lit=mix(tint,vec3(1),0.17);
    color+=lit*fresh*(0.65+pigment*0.28);
    if(particles_enabled) {
        vec2 dustUV=world/5.0+winding*t*0.08;
        vec2 cell=floor(dustUV),off=vec2(hash(cell+19.0),hash(cell+71.0));
        float dust=(1.0-smoothstep(0.025,0.16,length(fract(dustUV)-off)))*step(0.965,hash(cell));
        color+=mix(tint,vec3(1),0.65)*dust*(0.2+0.35*inward);
    }
    COLOR=vec4(color,alpha);
    }
}
