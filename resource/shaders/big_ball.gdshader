shader_type canvas_item;
uniform vec4 tint : source_color = vec4(1.0,0.2,0.3,1.0);
uniform float neutron : hint_range(0.0,1.0) = 0.0;

void fragment(){
    vec2 p=(UV-0.5)*2.28;
    float r=length(p);
    if(neutron<0.5){
        float edge=pow(smoothstep(0.38,0.97,r),3.0);
        float rim=exp(-abs(r-0.99)*48.0);
        float light=pow(max(0.0,dot(normalize(vec3(p,sqrt(max(0.0,1.0-r*r)))),normalize(vec3(-0.5,-0.7,0.35)))),9.0);
        vec3 col=tint.rgb*(0.008+edge*0.65+rim*0.48)+vec3(light*0.06+rim*0.14);
        float halo=exp(-max(0.0,r-1.0)*34.0)*0.32;
        COLOR=vec4(col,r<1.0?1.0:halo*(1.0-smoothstep(1.08,1.14,r)));
    } else {
        // Collapsed body: a small, intensely bright core inside a wide lensing halo.
        float core=1.0-smoothstep(0.36,0.52,r);
        float photon=exp(-abs(r-0.53)*62.0);
        float swirl=0.5+0.5*sin(atan(p.y,p.x)*10.0+TIME*3.0-r*30.0);
        float flicker=0.86+0.14*sin(TIME*17.0+r*57.0);
        vec3 cold=vec3(0.76,0.90,1.0);
        vec3 col=tint.rgb*0.08+cold*(core*(0.38+0.30*swirl)*flicker+photon*1.15)+tint.rgb*photon*0.45;
        float halo=exp(-max(0.0,r-0.54)*7.5)*0.42;
        float alpha=r<0.56?1.0:halo*(1.0-smoothstep(0.92,1.14,r));
        COLOR=vec4(col,alpha);
    }
}
