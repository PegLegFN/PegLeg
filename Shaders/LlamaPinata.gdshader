shader_type canvas_item;

uniform sampler2D packIcon:hint_default_black;
uniform bool useMask=false;
uniform sampler2D mask;

varying vec4 modulate;

void vertex()
{
	modulate = COLOR;
}

void fragment() {
	vec2 packUV=(UV-vec2(0.336,0.54))*7.5;
	packUV.y+=packUV.x*-0.05;
	packUV.y*=1.0-(packUV.x*0.1);
	//packUV = mod(packUV+vec2(10), vec2(1));
	vec4 packCol=texture(packIcon, packUV);
	COLOR.rgb=mix(COLOR.rgb,vec3(0.2890625,0.2578125,0.2265625)*modulate.rgb,packCol.r*packCol.a);
	if(useMask)
		COLOR.a*=texture(mask, UV).r;
}
