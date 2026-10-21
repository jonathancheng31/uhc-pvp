class_name Blocks
extends RefCounted
## Block types, and which texture tile each face of a block uses.

enum { AIR, GRASS, DIRT, STONE, WALL, PLANKS }

## Faces, in the order the mesher uses: +X, -X, +Y, -Y, +Z, -Z.
enum Face { POS_X, NEG_X, POS_Y, NEG_Y, POS_Z, NEG_Z }

## The texture atlas is a square grid of 16x16 pixel tiles.
const ATLAS_PATH := "res://assets/textures/blocks.png"
const ATLAS_COLUMNS := 4

const TILE_GRASS_TOP := 0
const TILE_GRASS_SIDE := 1
const TILE_DIRT := 2
const TILE_STONE := 3
const TILE_WALL := 4
const TILE_PLANKS := 5


static func is_solid(id: int) -> bool:
	return id != AIR


static func tile_for(id: int, face: int) -> int:
	match id:
		GRASS:
			if face == Face.POS_Y:
				return TILE_GRASS_TOP
			if face == Face.NEG_Y:
				return TILE_DIRT
			return TILE_GRASS_SIDE
		DIRT:
			return TILE_DIRT
		STONE:
			return TILE_STONE
		WALL:
			return TILE_WALL
		PLANKS:
			return TILE_PLANKS
	return TILE_STONE
