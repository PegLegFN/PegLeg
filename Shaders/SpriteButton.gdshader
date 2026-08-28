shader_type canvas_item;
#include "Includes/ScaleVector.gdshaderinc"

uniform vec2 ControlSize = vec2(100.0, 100.0);
uniform vec2 Offset0 = vec2(-9,-1);
uniform vec2 Offset1 = vec2(-3,4);
uniform vec2 Offset2 = vec2(4, 4);
uniform vec2 Offset3 = vec2(6,-9);
uniform float OutlineOffset = 5;
uniform float OutlineGap = 2;
uniform float progress:hint_range(0.0, 1.0)=1.0;
uniform bool special;
uniform bool lost;
uniform bool locked;

varying vec4 modulate;

void vertex() {
	modulate=COLOR;
	vec2 offset = vec2(0);
	vec2 extraOffset = vec2(0);
	switch(VERTEX_ID){
		case 1:
			offset = vec2(-OutlineOffset, OutlineOffset);
			extraOffset = Offset1;
			break;
		case 2:
			offset = vec2(OutlineOffset, OutlineOffset);
			extraOffset = Offset2;
			break;
		case 3:
			offset = vec2(OutlineOffset, -OutlineOffset);
			extraOffset = Offset3;
			break;
		default:
			offset = vec2(-OutlineOffset,-OutlineOffset);
			extraOffset = Offset0;
			break;
	}
	VERTEX += offset + (extraOffset * progress);
}

void fragment()
{
	COLOR=vec4(1.0);
	
	vec2 offsetSize = ControlSize+vec2(OutlineOffset*2.0);
	vec2 distVecBase=max(UV, vec2(1.0)-UV);
	vec2 distVec = (distVecBase*2.0*offsetSize)-(offsetSize*2.0);
	float dist = max(distVec.x, distVec.y);
	
	float outlineDist = clamp(min(dist+(OutlineOffset-OutlineGap)*2.0, -dist),0,1);
	float cardDist = clamp(-dist-(OutlineOffset)*2.0,0,1);
	
	vec4 rarityColor = special ? vec4(1) : modulate;
	
	float cardBase = (UV.y*100.0)-93.0;
	cardBase+=UV.x*(5.0+(progress*5.0));
	cardBase=clamp(cardBase,0,1);
	
	//float aspect = ControlSize.x/ControlSize.y;
	//float aspectCorrection = (1.0-aspect)*0.5;
	//vec2 aspectUV = vec2((UV.x*aspect)+aspectCorrection,UV.y);
	//float cardGradient = 1.0-distance(aspectUV,vec2(0.5,0.6));
	float cardGradient = clamp((UV.y*1.5)-0.5,0,1)*0.3;
	vec4 cardGradientCol = mix(vec4(0.8),rarityColor,cardGradient);
	if (lost){
		cardGradientCol=vec4(0.3);
	}
	
	vec4 cardColor = vec4(mix(cardGradientCol,rarityColor,cardBase));
	if(locked) {
		cardColor=vec4(mix(0.1,0.9,progress));
	}
	cardColor.a=1.0;
	
	COLOR = mix(vec4(1,1,1,0.75),cardColor,cardDist);
	COLOR.a *= cardDist+(outlineDist*progress);
}

//void light() {
//	// Called for every pixel for every light affecting the CanvasItem.
//	// Uncomment to replace the default light processing function with this one.
//}
