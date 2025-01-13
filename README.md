# InventorySaver
InventorySaver Plugin
Description
The InventorySaver plugin allows Rust server admins to save and restore players' inventories upon death, including items from their wear slots, belts, main inventory, backpacks, and supported plugins like Backpacks and Bag of Holding. It keeps multiple save slots per player, enabling detailed inventory management and recovery.

Features
Automatically saves a player's inventory upon death.
Stores up to 30 death slots per player with timestamps.
Supports restoring inventories, including plugin-backed containers (Backpacks and Bag of Holding).
Includes an admin command to list saved inventories for a player with slot numbers and timestamps.

Commands:
restoreinv <steamid> <slot>

Restores the specified inventory slot for a player by their Steam ID.

Example:
restoreinv 76561198012345678 1
This restores the first saved inventory for the player.

restoreinv <steamid> list
Lists all saved inventory slots for a player by their Steam ID, displaying the timestamp, number of items, and slot number.

Example:
restoreinv 76561198012345678 list
This outputs a list of the player's saved inventories.

Permissions
inventorysaver.use
Grants access to the commands for managing and restoring inventories.
