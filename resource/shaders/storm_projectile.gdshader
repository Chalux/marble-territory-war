shader_type canvas_item;
render_mode unshaded;
uniform float simulation_time;
varying vec2 board_position;
varying vec4 tint;
varying float span;

void vertex() {
    board_position = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
    tint = COLOR;
    span = 7.0 + 1.5 * sin(simulation_time * 7.0 + INSTANCE_CUSTOM.r * 4.0);
}

// Distance to the bowed wavefront, including its rounded ends.
float wave_distance(vec2 p) {
    float u = clamp(p.y / span, -1.0, 1.0);
    vec2 offset = p - vec2(-4.0 * u * u, span * u);
    vec2 tangent = normalize(vec2(-8.0 * u, span));
    float along = dot(offset, tangent);
    return length(offset - tangent * clamp(along, (-1.0-u)*span, (1.0-u)*span));
}

void fragment() {
    vec2 p = (UV - 0.5) * vec2(26.0, 28.0);
    float aa = max(fwidth(p.y) + fwidth(p.x), 0.1) * 0.35;
    float glow = (1.0 - smoothstep(4.0-aa, 4.0+aa, wave_distance(p+vec2(3.0,0.0)))) * 0.10;
    float front = (1.0 - smoothstep(0.8-aa, 0.8+aa, wave_distance(p))) * 0.65;
    float alpha = front + glow * (1.0-front);
    vec3 color = mix(tint.rgb, vec3(1.0), (0.10*glow*(1.0-front) + 0.35*front) / max(alpha, 0.00001));
    vec2 edge = min(board_position-vec2(760,20), vec2(1840,1060)-board_position);
    COLOR = vec4(color, alpha * smoothstep(0.0, 1.0, min(edge.x,edge.y)));
}
