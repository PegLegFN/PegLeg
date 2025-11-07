shader_type canvas_item;
#include "Includes/AdaptToAspect.gdshaderinc"
#include "Includes/CornerMask.gdshaderinc"

uniform vec2 ControlSize = vec2(100.0, 100.0);
uniform float Radius = 7.0;

float createMask(float gradient, float offset)
{
	return clamp(floor(gradient+offset)*(1.0-floor(gradient+offset-0.1)), 0, 1);
}

float getInsetLine(vec2 controlPos, float inset)
{
	float lineRad = Radius+2.0;
	float lineGrad = cornerGradient((controlPos-vec2(inset))/(ControlSize-vec2(inset*2.0)),lineRad,ControlSize-vec2(inset*2.0));
	return clamp(2.0-(distance((lineGrad*lineRad)+1.0, lineRad*0.5)*3.0), 0.0, 1.0) * (1.0-max(inset, 0.0)*0.1);
}

void fragment() {
	float mask = cornerMask(UV,Radius,ControlSize);
	vec2 controlPos = UV*ControlSize;


	float offset=mod(TIME*2.0, 1.0)*5.0;
	float lineMask = 0.0;
	lineMask+=getInsetLine(controlPos, offset-5.0);
	lineMask+=getInsetLine(controlPos, offset);
	lineMask+=getInsetLine(controlPos, offset+5.0);

	//COLOR.rgb = vec3(lineMask);
	COLOR.a *= mask*lineMask;
}
