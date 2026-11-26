shader_type canvas_item;
render_mode unshaded;
void fragment() {
    vec2 p=(UV-0.5)*36.0;
    float r=length(p),aa=max(fwidth(r),0.65);
    float outer=1.0-smoothstep(14.5,14.5+aa,r);
    float facing=0.65+0.35*dot(normalize(p+vec2(0.001)),normalize(vec2(-0.5,-0.8)));
    float rings=exp(-pow((r-12.5)/0.65,2.0))*0.65;
    rings+=exp(-pow((r-9.5)/0.55,2.0))*0.72;
    rings+=exp(-pow((r-7.5)/0.55,2.0))*0.26;
    float dome=sqrt(max(0.0,1.0-r*r/42.25));
    float spot=exp(-dot(p+vec2(1.7,2.6),p+vec2(1.7,2.6))/9.0);
    vec3 color=vec3(0.045)+vec3(0.84,0.89,0.92)*(rings*facing+dome*0.19+spot*0.40);
    COLOR=vec4(color,outer);
}
