shader_type canvas_item;
#include "Includes/AdaptToAspect.gdshaderinc"
#include "Includes/CardPackIcon.gdshaderinc"

uniform vec2 ControlSize = vec2(100.0, 100.0);
uniform bool Fit;
uniform float VerticalShift = 0.0;
uniform bool IsHero;
uniform bool IsSchematic;
uniform bool IsPack;
uniform sampler2D Noise;
uniform sampler2D HeroTriangles : hint_default_white;
uniform sampler2D PackIcon : hint_default_transparent;

varying vec4 modulate;

void vertex() {
	modulate = COLOR;
}

float gridFunc(float input, float line, float grid)
{
	return 1.0-clamp(abs(mod(input, grid)-grid*0.5)*(1.0/grid)*line, 0, 1);
}

float radial(vec2 uv, vec2 from)
{
	vec2 src = uv-from;
	return atan(src.x/src.y);
}

const float triScale = 1.0;
const float invTriScale = 1.0/triScale;
vec4 triTex(vec2 uv, float speed)
{
	return texture(HeroTriangles, mod(uv+vec2(0.0,TIME*speed), invTriScale)*triScale);
}

void fragment() {
	vec2 adaptedUV = adaptToAspect(ControlSize, vec2(0.5, VerticalShift), 1.0/TEXTURE_PIXEL_SIZE, UV, Fit);
	vec2 backgroundUV = adaptToAspect(ControlSize, vec2(0.5), vec2(1.0), UV, false);
	vec4 texCol = texture(TEXTURE, adaptedUV);
	if(adaptedUV.y > 1.0 || adaptedUV.y < 0.0){
		texCol.a = 0.0;
	}
	if(IsPack) {
		texCol = MixPackIcon(texCol, adaptedUV, PackIcon);
	}
	vec4 backgroundCol = modulate;

	if(IsHero)
	{
		float radialVal = radial(backgroundUV, vec2(0.5,1.05));
		float beams = clamp(1.1-floor(abs(radialVal)*5.0)/10.0, 0.5, 1);
		float glow = clamp(1.0-(distance(backgroundUV, vec2(0.5, 0.7))), 0, 1);
		float result = glow*beams*1.75;

		float tri1 = min(triTex(backgroundUV, 0.02).r * 3.0, 1);
		float tri2 = min(triTex(backgroundUV, 0.025).g * 4.0, 1);
		float tri3 = min(triTex(backgroundUV, 0.015).b * 4.0, 1);
		//backgroundCol = mix(backgroundCol,modulate*0.75 ,  tri1);
		//backgroundCol = mix(backgroundCol,modulate*1.0,  tri2);
		//backgroundCol = mix(backgroundCol,mix(modulate, vec4(1), 0.25) ,  tri3);

		result = mix(result, 1.0, tri3);
		result = mix(result, 1.5, tri2);
		result = mix(result, 0.75, tri1);

		backgroundCol = min(result, 1)*mix(modulate, vec4(1),max(0, result-1.0));
		float darkSlice = min(1.0-min((backgroundUV.y+(backgroundUV.x*0.15)-0.25)*100.0, 1), 1);
		backgroundCol = mix(backgroundCol, backgroundCol*0.7, darkSlice);
	}
	else if(IsSchematic)
	{
		float noiseVal = texture(Noise, backgroundUV).r;
		float wave = clamp(((backgroundUV.y-0.1)-noiseVal*0.3)*100.0, 0, 1)*0.1;

		float xGrid = gridFunc(backgroundUV.x-0.5, 20, 0.1);
		float yGrid = gridFunc(backgroundUV.y, 20, 0.1);
		float fullGrid = min(xGrid+yGrid, 1)*0.3;

		float result = min(1, fullGrid+(wave*(1.0-backgroundUV.y)))+0.1;
		backgroundCol = mix(vec4(1), modulate, result);
	}
	else
	{
		backgroundCol = mix(modulate, modulate*0.5, distance(backgroundUV, vec2(0.5))*1.42);
	}
	COLOR.rgb = mix(backgroundCol.rgb, texCol.rgb, texCol.a);
	COLOR.a=1.0;
	//COLOR = mix(backgroundCol, texCol, 0.0);
}
