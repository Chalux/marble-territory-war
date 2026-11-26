shader_type canvas_item;
render_mode unshaded;
uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_linear;
vec3 bright(vec3 c) {
    float peak=max(max(c.r,c.g),c.b);
    float luminance=dot(c,vec3(0.2126,0.7152,0.0722));
    return c*smoothstep(0.62,1.0,peak)*smoothstep(0.2,0.85,luminance);
}
void fragment() {
    vec3 original=texture(screen_texture,SCREEN_UV).rgb;
    vec3 bloom=vec3(0);float weight=0.0;
    // Merge the first and second Gaussian taps on each side at their weighted centre.
    const float offsets[5]=float[5](-9.6,-4.166,0.0,4.166,9.6);
    const float weights[5]=float[5](0.08046,1.08206,1.0,1.08206,0.08046);
    for(int y=0;y<5;y++)for(int x=0;x<5;x++) {
        vec2 offset=vec2(offsets[x],offsets[y])*SCREEN_PIXEL_SIZE;
        float w=weights[x]*weights[y];
        bloom+=bright(texture(screen_texture,SCREEN_UV+offset).rgb)*w;weight+=w;
    }
    float peak=max(max(original.r,original.g),original.b);
    COLOR=vec4(original+bloom/max(weight,0.001)*0.38*(1.0-clamp(peak,0.0,1.0)*0.75),1);
}
