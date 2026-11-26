shader_type canvas_item;
uniform vec4 tint : source_color = vec4(1.0);
uniform float simulation_time = 0.0;
uniform bool particles_enabled = true;
uniform float energy_pulse = 0.0;
uniform float phase_offset = 0.0;
float hash(vec2 p){vec3 q=fract(vec3(p.xyx)*0.1031);q+=dot(q,q.yzx+33.33);return fract((q.x+q.y)*q.z);}
void fragment() {
    // 1.22 padding in the presenter places the actual sphere at radius 1.
    vec2 p=(UV-0.5)*2.44;
    float r=length(p), a=atan(p.y,p.x);
    float clock=simulation_time+phase_offset;
    float inside=1.0-smoothstep(0.985,1.0,r);
    float rim=exp(-abs(r-0.988)*57.0);
    float halo=exp(-abs(r-0.99)*20.0);
    float shell=pow(clamp(r,0.0,1.0),6.0)*inside;
    float phase=a*15.0+r*46.0+sin(a*5.0-simulation_time*0.32)*2.0;
    float ripple=pow(0.5+0.5*sin(phase-simulation_time*0.6),16.0);
    float highlight=pow(max(0.0,dot(normalize(p+vec2(0.0001)),normalize(vec2(-0.5,-0.8)))),12.0);
    vec2 starUV=(p+vec2(sin(simulation_time*0.15),cos(simulation_time*0.12))*0.035)*40.0;
    vec2 cell=floor(starUV);
    float seed=hash(cell);
    float sparkle=(1.0-smoothstep(0.04,0.25,length(fract(starUV)-vec2(hash(cell+7.0),hash(cell+19.0)))))*step(0.91,seed);
    sparkle*=0.3+0.7*pow(0.5+0.5*sin(simulation_time*1.3+seed*80.0),3.0);
    if(!particles_enabled)sparkle=0.0;
    float glass=pow(0.5+0.5*sin(a*3.0-r*8.0+simulation_time*0.15),5.0)*shell;
    float shell_phase=r*145.0+sin(a*7.0+simulation_time*0.3)*1.5+sin(a*13.0-simulation_time*0.2)*0.6;
    float shell_threads=pow(0.5+0.5*sin(shell_phase),22.0)*smoothstep(0.76,0.96,r)*inside;
    float z=sqrt(max(0.0,1.0-r*r));
    vec2 sphere=p/(0.45+z);
    float stream=sin(sphere.x*11.0+sin(sphere.y*6.0+simulation_time*0.22)*1.5)+sin(sphere.y*13.0-simulation_time*0.18);
    float caustic=pow(1.0-smoothstep(0.0,0.24,abs(stream)),2.0)*inside;
    float haze=(0.5+0.5*sin(p.x*7.0+p.y*3.0+simulation_time*0.15))*inside;
    float orbit=pow(0.5+0.5*cos(a-clock*0.85+sin(clock*0.43)*0.3),18.0);
    float counter=pow(0.5+0.5*cos(a*2.0+clock*0.63),24.0);
    float breathing=0.82+0.18*sin(clock*1.8+sin(a*3.0-clock)*0.6);
    float wave=exp(-pow((r-(0.25+fract(clock*0.32)*0.72))*28.0,2.0));
    wave*=sin(fract(clock*0.32)*3.14159)*inside;
    float impact=energy_pulse*exp(-abs(r-(0.92-energy_pulse*0.45))*23.0)*inside;
    vec3 color=tint.rgb*(0.018+shell*0.22+rim*(0.55+breathing*0.25)+ripple*shell*0.09+glass*0.12);
    color+=mix(tint.rgb,vec3(1),0.4)*(orbit*rim*0.9+counter*shell*0.25+wave*0.16+impact*0.7);
    color+=tint.rgb*shell_threads*0.12;
    color+=tint.rgb*(caustic*(0.016+shell*0.14)+haze*0.015);
    color+=vec3(rim*(0.035+highlight*0.22)+sparkle*inside*1.55);
    // Dark glass attenuates the territory beneath it, leaving the rim readable.
    float alpha=inside*(0.90+shell*0.04)+halo*(0.45+breathing*0.2)+sparkle*inside*0.5;
    COLOR=vec4(color,clamp(alpha,0.0,0.95));
}
