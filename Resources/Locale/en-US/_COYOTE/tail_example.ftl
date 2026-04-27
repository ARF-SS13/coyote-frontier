# Hi Jess! This is an example of how to make a tail marking.
# In this file we will show how to make two different kinds of tail markings:
# 1. A simple tail marking that uses the old system (please dont use this)
# 2. A more complex tail marking that uses the new system (recommended)
# This process involves three files:
# 1. The .yml file (this file) which defines the marking and its properties
# 2. The .rsi file which contains the sprites for the marking
# 3. The .ftl file which contains the text to show in the customization menu
# You will need to make all three files for your marking to work properly.
# This file will focus on the .ftl file

# Continued from Resources/Prototypes/_Floof/Entities/Mobs/Customization/markings/tail_example.yml
# regarding the Simple tail marking, this is how you would define the text for it:
# For this, we will need multiple entries:
# 1. The name that shows up in the customization menu for the marking itself
#    This is formatted as such: marking-[marking ID] = [display name]
# 2+. The name(s) that show up in the customization menu for the individual layers of the marking
#     This is formatted as such: marking-[marking ID]-[sprite state] = [display name]

# In this example, our tail's ID is "TailFox"
# The sprite states are "tail_fox_primary" and "tail_fox_secondary"
# So the entries would look like this:
marking-TailFox = Fox Tail
marking-TailFox-tail_fox_primary = Fox Tail (Primary)
marking-TailFox-tail_fox_secondary = Fox Tail (Secondary)
# easy huh? Now you can make a simple tail marking!

# Now, for the more complex tail marking, we will use the new system.
# This is essentially the same as the old system, but there tend to be more layers.
# In this example, our tail's ID is "TailBatl"
# The sprite states are "m_tail_batl_BEHIND_primary", "m_tail_batl_BEHIND_secondary",
# "m_tail_batl_FRONT_primary", and "m_tail_batl_FRONT_secondary"
# So the entries would look like this:
marking-TailBatl = Bat Tail, Long
marking-TailBatl-m_tail_batl_BEHIND_primary = Bat Tail, Long (Primary)
marking-TailBatl-m_tail_batl_BEHIND_secondary = Bat Tail, Long (Secondary)
marking-TailBatl-m_tail_batl_FRONT_primary = Bat Tail, Long (Primary)
marking-TailBatl-m_tail_batl_FRONT_secondary = Bat Tail, Long (Secondary)
# So you may have noticed that we define a name for each layer, even if some of them
# would be hidden, due to colorLinks. This is intentional, and it is a fallback that
# keeps the linters from complaining and aids with searchability.

# HUGE NOTE AOUT FTL FILES:
# Every entry MUST be unique across the entire game.
# This means that there can only ever be one "marking-TailFox" entry defined in the entire game.
# If there are duplicates *anywhere* in *any* FTL file, the game will throw an error and refuse to start.
# If your game suddenly breaks and the won't tell you why, check for duplicate entries in your FTL files.

# Another little note:
# Where should these files go?
# Put them in: Locale/en-US/_Coyote/

# ANother note:
# If your marking has a name that looks wierd, like ingame it looks like "marking-TailFox" instead of "Fox Tail",
# then check for typos in both the .yml and .ftl files to make sure the IDs and state names match exactly.
# If you have any questions, feel free to ask me!
# - Dan 'Superlagg' Kelly

