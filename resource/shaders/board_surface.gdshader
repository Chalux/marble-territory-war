shader_type canvas_item;
render_mode unshaded;

float rounded_box(vec2 p, vec2 half_size, float radius) {
    vec2 q=abs(p)-half_size+radius;
    return length(max(q,vec2(0)))+min(max(q.x,q.y),0.0)-radius;
}
float line(float d,float center,float width) {
    return 1.0-smoothstep(width,width+max(fwidth(d),0.65),abs(d-center));
}
void fragment() {
    vec2 pixel=UV*vec2(680,1080);
    vec2 p=pixel-vec2(340,540);
    float d=rounded_box(p,vec2(328,528),38.0);
    // Continuous machined moulding: recessed channel, broad bevel, narrow highlight.
    float metal=0.085;
    metal+=0.16*exp(-pow((d+3.0)/4.0,2.0));
    metal+=0.20*exp(-pow((d+15.0)/3.2,2.0));
    metal+=0.22*line(d,-18.0,0.45)+0.17*line(d,0.0,0.45);
    metal+=0.11*line(d,-6.0,0.35)+0.09*line(d,-11.5,0.35);
    metal-=0.045*line(d,-8.5,1.2);
    float lighting=0.8+0.2*clamp((-p.x-p.y)/650.0,0.0,1.0);
    vec3 frame=vec3(metal)*vec3(0.96,1.0,1.02)*lighting;
    float recess=1.0-smoothstep(-21.0,-19.0,d);
    float wash=exp(-length((UV-vec2(0.36,0.15))*vec2(2.2,1.1))*2.0);
    vec3 felt=vec3(0.066,0.071,0.077)+wash*0.018;
    vec2 grid=abs(mod(pixel-vec2(28,30),64.0)-32.0);
    float gridline=1.0-smoothstep(0.35,1.1,min(grid.x,grid.y));
    felt+=gridline*0.011;
    float inner_shadow=exp(-abs(d+20.0)/7.0)*recess;
    vec3 color=mix(frame,felt,recess)-inner_shadow*0.025;
    // Keep the squared, cut-corner outer casing visible beneath this insert.
    COLOR=vec4(color,1.0-smoothstep(0.0,1.0,d-2.0));
}
