shader_type canvas_item;
render_mode unshaded;
uniform float simulation_time=0.0;
varying vec2 board_position;
varying vec4 shot_color;
varying float phase;

void vertex() {
    board_position=(MODEL_MATRIX*vec4(VERTEX,0.0,1.0)).xy;
    shot_color=COLOR;
    phase=INSTANCE_CUSTOM.r*6.283185;
}

void fragment() {
    vec2 p=(UV-0.5)*vec2(48.0,18.0);
    float behind=14.0-p.x;
    float age=clamp(behind/29.0,0.0,1.0);
    float drift=sin(age*8.0-simulation_time*17.0+phase)*age*0.55;
    float width=mix(2.0,0.18,pow(age,0.65));
    float wake=exp(-pow((p.y-drift)/width,2.0)*1.6)*pow(1.0-age,1.8);
    wake*=smoothstep(-0.5,2.5,behind);
    // Soft rounded head and a tapered, subtly moving envelope instead of line strokes.
    vec2 head=(p-vec2(14.0,0.0))/vec2(3.5,2.4);
    float body=exp(-dot(head,head)*1.8);
    float core=exp(-dot(head,head)*6.0);
    float halo=exp(-dot(head,head)*0.35)*0.18;
    float alpha=clamp(body*0.95+wake*0.65+halo,0.0,1.0);
    vec3 color=mix(shot_color.rgb,vec3(1.0),core*0.85+body*0.12);
    // Clip soft pixels as well as the body when changing direction at a wall.
    vec2 edge=min(board_position-vec2(760,20),vec2(1840,1060)-board_position);
    alpha*=smoothstep(0.0,1.0,min(edge.x,edge.y));
    COLOR=vec4(color,alpha);
}
