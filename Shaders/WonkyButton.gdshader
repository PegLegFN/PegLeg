shader_type canvas_item;
#include "Includes/ScaleVector.gdshaderinc"

uniform vec2 ControlSize = vec2(100.0, 100.0);
uniform vec2 Offset0 = vec2(-9,-1);
uniform vec2 Offset1 = vec2(-3,4);
uniform vec2 Offset2 = vec2(4, 4);
uniform vec2 Offset3 = vec2(6,-9);
uniform float progress:hint_range(0.0, 1.0)=1.0;
uniform bool outline=false;

varying vec4 modulate;

void vertex() {
	modulate=COLOR;
	vec2 offset = vec2(0);
	switch(VERTEX_ID){
		case 1:
			offset=Offset1;
			break;
		case 2:
			offset=Offset2;
			break;
		case 3:
			offset=Offset3;
			break;
		default:
			offset=Offset0;
			break;
	}
	VERTEX += offset * progress;
}

void fragment()
{
	COLOR=vec4(1.0);
	if(outline)
	{
		vec2 distVecBase=max(UV, vec2(1.0)-UV);
		vec2 distVec = (distVecBase*2.0*ControlSize)-(ControlSize*2.0);
		float dist = max(distVec.x, distVec.y)+10.0;
		COLOR.a=clamp(dist,0,1);
	}
	COLOR*=modulate;
	
}

//void light() {
//	// Called for every pixel for every light affecting the CanvasItem.
//	// Uncomment to replace the default light processing function with this one.
//}
