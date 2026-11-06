shader_type canvas_item;

uniform vec2 ControlSize = vec2(100.0, 100.0);
uniform vec2 offset = vec2(0.0, 0.0);
uniform sampler2D noise;
uniform sampler2D swirl;
uniform sampler2D flow;
uniform sampler2D detailMask;
uniform sampler2D detailNormal;

varying vec4 modulate;

void vertex(){
	modulate=COLOR;
}

void fragment() {
	vec2 adaptedUV = ((UV*ControlSize)-offset)/500.0;
	
	vec2 offsetUV = mod(adaptedUV*0.7+(TIME*vec2(-0.06,-0.05)),1);
	vec2 swirlDisplace = texture(flow, offsetUV).rg-vec2(0.5);
	vec2 offsetUVLarge = mod(adaptedUV*0.4+(TIME*vec2(0.02,0.03)),1);
	vec2 swirlDisplaceLarge = texture(flow, offsetUVLarge).rg-vec2(0.5);
	vec2 swirledUV = (adaptedUV*1.0)+(swirlDisplace*0.15)+(swirlDisplaceLarge*0.15);
	
	vec3 swirlNormal = texture(swirl, mod(swirledUV, 1)).rgb;
	float detailMaskVal = texture(detailMask, mod(swirledUV*2.5, 1)).r;
	swirlNormal = normalize(swirlNormal * mix(vec3(1.0), texture(detailNormal, mod(swirledUV*2.5, 1)).rgb, detailMaskVal));
	
	float swirlMask = clamp(dot(swirlNormal, normalize(vec3(-0.40,1.0,0.0))*1.0),0,1);
	
	float swirlEdge = clamp((COLOR.a*1.35)-swirlMask,0,1);
	float swirlLight = dot(swirlNormal, normalize(vec3(-0.25,0.25,0.45)));
	COLOR.rgb = swirlLight* modulate.rgb;
	COLOR.rgb *= 0.7 + detailMaskVal * 0.3;
	COLOR.rgb *= 0.5 + swirlEdge * 0.5;
	
	float noiseVal = texture(noise, mod(swirledUV+(TIME*vec2(0.01,-0.05)), 1)).r;
	noiseVal *= texture(noise, mod(swirledUV+(TIME*vec2(-0.02,0.05)), 1)).r;
	
	COLOR.a = clamp((COLOR.a*2.0)-(swirlMask*1.7+noiseVal*0.2),0,1)*modulate.a;
	//COLOR.rgb = vec3(COLOR.a);
	//COLOR.a = 1.0;
}
