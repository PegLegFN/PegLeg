shader_type canvas_item;
#include "Includes/AdaptToAspect.gdshaderinc"
#include "Includes/CornerMask.gdshaderinc"
#include "Includes/HSVToRGB.gdshaderinc"

uniform sampler2D TrianglePattern;
uniform vec2 ControlSize = vec2(100.0, 100.0);
uniform vec2 ShiftDirection = vec2(0.5,0.5);
uniform float CornerRadius = 10.0;
uniform bool ColorMode = false;
uniform vec4 Color : source_color = vec4(1);
uniform float time = 0.0;
uniform bool rainbow;

varying vec4 modulate;

void vertex()
{
	modulate=COLOR;
}

void fragment() {
	if(ColorMode)
	{
		vec2 patternTexRes=1.0/vec2(textureSize(TrianglePattern, 0));
		vec2 patternUV = adaptToAspect(ControlSize, vec2(0,0), patternTexRes, UV, true);
		
		vec2 moddedPatternUV = patternUV*0.5;
		moddedPatternUV.y+=floor(moddedPatternUV.x)*0.25;
		moddedPatternUV = mod(moddedPatternUV, vec2(1,1));
		float patternResult = texture(TrianglePattern, moddedPatternUV).r;
		
		float gradient = ((patternResult+0.2)*6.0)+patternUV.x+patternUV.y-(time*8.0);
		gradient = mod(gradient+20.0, 40)-20.0;
		gradient = 1.0-abs(gradient);
		gradient=clamp(gradient,0,1);
		
		COLOR = rainbow ? vec4(hsv_to_rgb(vec3((patternResult*2.0+patternUV.x-TIME*2.0)*0.07+patternUV.y*0.01,1,1)),1) : Color;
		COLOR.rgb *= 0.6+((patternResult+gradient*2.0)*0.2);
	}
	else
	{
		vec2 texRes=1.0/TEXTURE_PIXEL_SIZE;
		vec2 adaptedUV = adaptToAspect(ControlSize, ShiftDirection,texRes,UV,false);
		COLOR = texture(TEXTURE, adaptedUV);
	}
	//COLOR.rgb=vec3(gradient);
	//COLOR.xy = mod(patternUV, vec2(1,1));

	COLOR.a *= cornerMask(UV,CornerRadius,ControlSize);
	COLOR *= modulate;
}