"""Generate the 15-minute film THE LAST GARDENER on KT H200 through Comfy MCP.

The program uses MiniMax H3 Ref2VA for fresh compositions while preserving the
two leads from act-specific reference frames.  It deliberately follows the
official six-section full-reference prompt format instead of treating an image
as a fake pan/zoom source video.
"""

from __future__ import annotations

import argparse
import asyncio
import copy
import hashlib
import json
import os
import re
import shutil
import subprocess
import tempfile
import urllib.request
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


MCP_BIN = "/home/work/media-lab-data/minimax-h3/env/comfy-mcp/bin/comfy-mcp"
COMFY_BIN = "/home/work/media-lab-data/minimax-h3/env/comfy-mcp/bin/comfy"
COMFY_URL = "http://127.0.0.1:8189"
DEFAULT_ROOT = Path("/home/work/media-lab-data/minimax-h3/runs/last-gardener")
TEMPLATE_URL = (
    "https://raw.githubusercontent.com/Comfy-Org/workflow_templates/"
    "main/templates/video_minimax_h3_r2v.json"
)
TEMPLATE_SHA256 = "45a3f213430d3b4db6bdbd1873f6b0c09ea81aafdfd0fd5ec2a26d355c5c5941"
R2V_LORA = "minimax_h3_ref2v_turbo_4step_v0.1_comfyui_bf16.safetensors"
WIDTH, HEIGHT, FPS, SECONDS, LENGTH = 1344, 768, 24, 15, 362
SUCCESS_STATUSES = frozenset({"completed", "complete", "success", "succeeded", "done"})
FAILURE_STATUSES = frozenset({"failed", "error", "cancelled", "canceled"})
STATE_VERSION = 2
MIN_FREE_BYTES = 1024**3
EXPECTED_TEMPLATE_NODES = {
    92: "SaveVideo", 115: "ResolutionSelector", 124: "BasicScheduler", 129: "RandomNoise",
    132: "PrimitiveFloat", 136: "MiniMaxH3ReferenceToVideo", 137: "LoadImage",
    138: "PrimitiveStringMultiline", 139: "LoadImage", 143: "PrimitiveInt",
    144: "PrimitiveInt", 145: "LoraLoaderModelOnly", 146: "PrimitiveBoolean",
}


@dataclass(frozen=True)
class Beat:
    title: str
    action: str
    camera: str
    sound: str
    music: str
    dialogue: str = ""


ACTS: tuple[tuple[str, str, tuple[Beat, ...]], ...] = (
    (
        "The Seed Wakes",
        "act1-orbital-greenhouse.png",
        (
            Beat("Blue Earth", "Earth's night side rolls beneath the abandoned orbital greenhouse while hundreds of dormant seed capsules hang in darkness; Hana crosses the overgrown aisle and notices one capsule blinking gold.", "A slow exterior-to-interior crane move passes through fractured glass and settles behind Hana.", "Distant station groans, ventilation, leaves brushing glass, one crystalline seed pulse.", "Near-silent low strings and glass harmonica, ending on one warm cello note."),
            Beat("The Last Watering", "Hana hand-waters the final living fern from a dented metal flask; Ilyeon silently repairs a leaking irrigation pipe and catches each floating droplet with precise fingers.", "Intimate shoulder-height lateral dolly with foreground leaves crossing naturally.", "Water beads, old pump clicks, porcelain fingers on brass, soft breathing.", "Sparse felt piano with a restrained two-note motif.", "Hana (S1): <d>[Korean] 오늘도 버텨 줬구나.</d>"),
            Beat("Golden Germination", "The black seed splits open in Hana's palm and a luminous double leaf unfolds; its light travels through root-like wiring under the floor and wakes distant capsules one by one.", "Macro on the seed opens into a circular orbit around both faces, then racks focus to the waking greenhouse.", "Shell crack, electrical root hum, glass capsules chiming in sequence.", "Low choir breath and bowed glass gradually enter."),
            Beat("A Voice in the Roots", "The sprout emits a patterned pulse that Ilyeon translates into a map of Earth's drowned coast; Hana recognizes coordinates belonging to the world's last tree.", "Controlled push from projected map to Hana's eyes with genuine parallax.", "Soft data tones, pulse rhythm, failing projector fan.", "Muted synth arpeggio under sustained cello.", "Ilyeon (S2): <d>[Korean] 신호가 아래에서 오고 있어요. 지구에서요.</d>"),
            Beat("Orbit Decays", "Warning lights ignite as micrometeor damage makes the greenhouse lose altitude; soil drifts upward and the planet grows visibly larger through the dome.", "A calm locked frame becomes a slow Dutch roll matching the station's rotation.", "Alarm klaxon kept low, bolts ticking, soil grains against glass, deep structural boom.", "Sub-bass pulse starts beneath the main motif."),
            Beat("The Empty Ark", "Hana walks through an archive of empty cryogenic berths, touches one frost-covered nameplate, then seals the golden sprout inside a brass field compass.", "Long symmetrical tracking shot that ends in an extreme close-up of the compass latch.", "Boots in a hollow corridor, frost cracking, brass latch, distant alarm.", "Solo cello, no percussion.", "Hana (S1): <d>[Korean] 씨앗은 보관하는 게 아니라, 심는 거야.</d>"),
            Beat("Ilyeon's Choice", "Ilyeon removes a maintenance key from his own chest and offers it to unlock the forbidden descent pod; moss around the cracked panel trembles as if alive.", "Medium two-shot with a slow half orbit and a final hold on the offered key.", "Servo movement, chest panel release, quiet leaf rustle.", "Piano motif returns with a warm viola answer."),
            Beat("Storm Window", "They cross an exposed glass bridge while aurora and debris burn above Earth; a panel ruptures behind them and air tears loose papers into space as Ilyeon shields Hana.", "Fast backward steadicam retreat, one physical impact jolt, no cut.", "Rising wind, glass fracture, emergency shutters, cloth snapping.", "Tight string ostinato with one bass-drum impact."),
            Beat("Seed Vault Goodbye", "Hana opens every remaining seed drawer and releases the capsules into the greenhouse soil rather than letting them die locked away; tiny status lights scatter like stars.", "Overhead descent into a close low glide following capsules across soil.", "Metal drawers, seeds raining softly, alarm muffled behind doors.", "Wordless female humming with distant piano."),
            Beat("Descent Pod", "Hana and Ilyeon strap into a scratched two-seat pod while the sprout compass projects a golden route through storm clouds; the docking clamps refuse to release.", "Tight handheld cockpit framing with subtle vibration and motivated focus shifts.", "Harness buckles, computer relays, rain beginning on the hull.", "Rhythmic low strings gathering momentum.", "Ilyeon (S2): <d>[Korean] 귀환 연료는 없습니다.</d> Hana (S1): <d>[Korean] 돌아올 생각 없어.</d>"),
            Beat("Manual Release", "Ilyeon reaches outside through an emergency sleeve and forces the frozen clamp open as the pod drops; Hana catches his wrist just before decompression pulls him away.", "Exterior-mounted camera rotates with the pod, then settles on their locked hands through the window.", "Metal shriek, explosive clamp release, heartbeat-like hull impacts.", "Full strings surge and abruptly cut at release."),
            Beat("Falling Home", "The pod plunges toward Earth's cloud deck, leaving the greenhouse small behind it; Hana and Ilyeon watch the golden sprout remain perfectly upright while lightning fills the cockpit.", "Continuous pullback from the sprout to the window and out into a vast orbital wide shot.", "Atmospheric roar, thunder, vibrating panels, calm seed tone.", "Main theme stated by cello and French horn."),
        ),
    ),
    (
        "The Drowned Archive",
        "act2-drowned-city.png",
        (
            Beat("Rain Landing", "The pod splashes into a flooded avenue at dawn; Hana surfaces with the compass held above water while Ilyeon tears the jammed hatch free.", "Waterline camera retreats as the pod settles and towers emerge through rain.", "Underwater thud, bubbles, heavy rain, metal hatch tearing.", "Low percussion and breathy woodwinds."),
            Beat("Solar Sail", "They convert the pod's heat shield into a small sailboat and move between half-submerged buildings toward the dead tree.", "Wide lateral tracking from another invisible boat with layered reflections.", "Sail rope, water against hull, distant birds, creaking towers.", "Gentle plucked strings over the seed motif."),
            Beat("The Shadow Below", "A vast school of silver fish moves beneath the boat like one creature; its passing shadow briefly resembles roots reaching toward the compass.", "Top-down crane that lowers to Hana's cautious eye level.", "Water rush below, hull knock, compass chime.", "Muted bass clarinet and suspended strings."),
            Beat("Apartment Reef", "They pass an open apartment high above the old street, now an island garden tended by an elderly survivor who silently gives Hana a jar of clean soil.", "Slow telephoto drift from domestic details to the exchange across boats.", "Rain on dishes, small wind chimes, oar against concrete.", "Warm solo viola, almost no bass.", "Survivor (S3): <d>[Korean] 나무가 아직 꿈을 꿔요.</d>"),
            Beat("Subway Mouth", "The compass points beneath the water; Ilyeon dives into a submerged subway entrance while Hana anchors the boat and follows his light through dark water.", "Camera submerges in one continuous move and follows from behind.", "Surface rain folds into underwater rumble, bubbles, distant metal groan.", "Filtered synth pulse, slow and tense."),
            Beat("Memory Carriage", "Inside a flooded train, old passenger screens flicker with ordinary family recordings; Ilyeon watches a child wave at the camera and involuntarily mirrors the gesture.", "Weightless glide down the carriage, ending on Ilyeon's reflected porcelain face.", "Water creaks, muffled archival laughter, electronics sputter.", "Detuned music-box notes under cello harmonics."),
            Beat("Air Pocket", "They surface into a sealed station chamber filled with roots and trapped air; Hana discovers living white fungi converting rust into soil.", "Lamp-lit circular dolly reveals the ecosystem piece by piece.", "Gasping breath, dripping water, fungi crackle, distant root vibration.", "Quiet wonder theme on celesta and bass flute."),
            Beat("The Map Wall", "Ilyeon powers an ancient city map; golden root signals converge on the dead tree but a red rotating storm front closes over the route.", "Static architectural wide slowly pushes into the map's branching lines.", "Transformer buzz, relays, storm thunder through concrete.", "Measured timpani heartbeat enters."),
            Beat("Current", "A surge floods the chamber; Hana is swept through the station while Ilyeon plants his feet and extends a cable to pull her back.", "Fast water-level tracking with real spray and one sustained action line.", "Roaring water, cable spool, Hana's strained breath, concrete impacts.", "Urgent strings without heroic brass."),
            Beat("Compass Lost", "The brass compass slips into a deep flooded shaft; Ilyeon dives after it as its golden light shrinks below, then returns with the sprout protected between both hands.", "Long vertical underwater descent and ascent, no cut.", "Bubbles, pressure groan, muffled seed pulse becoming clearer.", "Solo high violin descends then resolves upward."),
            Beat("Old Broadcast", "A rooftop radio catches the last greenhouse transmission: orbit failure is accelerating and debris will strike the coast before night.", "Wind-battered close two-shot circling to reveal the bright orbital streak overhead.", "Radio static, clipped synthetic warning, gusting rain.", "Low horn note with ticking percussion.", "Station voice (S4): <d>[Korean] 궤도 붕괴까지 여섯 시간.</d>"),
            Beat("Toward the Tree", "Hana raises the repaired sail and the boat enters open water; the dead tree and its turbine crown fill the horizon as lightning strikes behind it.", "Epic low bow-mounted push toward the horizon with strong water parallax.", "Sail snaps open, waves, thunder roll, compass tone locks steady.", "Main theme accelerates into determined strings."),
        ),
    ),
    (
        "Falling Sky",
        "act3-orbital-collapse.png",
        (
            Beat("Debris Dawn", "Burning greenhouse fragments cross the sky like a meteor shower while Hana and Ilyeon shelter beneath an overturned ferry.", "Long-lens compression of falling debris becomes a close grounded pan to the pair.", "Distant sonic booms, rain on steel, debris hiss.", "Low brass clusters under a thin cello line."),
            Beat("Seed Rain", "Thousands of released orbital seed capsules survive reentry and drift down on silver parachutes, turning the catastrophe into a luminous rain.", "Camera tilts from Hana's face to a vast sky filled with descending capsules.", "Parachute fabric, capsule chimes, rain easing.", "Choir enters softly, neither triumphant nor sad."),
            Beat("Collector Drones", "Abandoned municipal drones wake and begin capturing the falling capsules as hazardous debris; Ilyeon steps into the open and broadcasts an override.", "Low circular tracking around Ilyeon as drones form a threatening halo.", "Rotor swarm, warning tones, Ilyon's clear electronic pulse.", "Dry electronic rhythm against strings."),
            Beat("False Memory", "The drones project Ilyeon's original factory record, revealing he was built to dismantle the root network; Hana watches him struggle against a dormant command.", "Projection light flickers across a locked medium two-shot, slowly tightening.", "Archive voice, servo stutter, rainwater dripping from metal.", "Piano motif becomes dissonant.", "Ilyeon (S2): <d>[Korean] 제가 나무를 죽였어요.</d>"),
            Beat("Refusal", "Ilyeon crushes the command transmitter in his hand and the drones drop harmlessly into shallow water; moss rapidly covers the damaged fingers.", "Close macro on the hand widens into a steady hero frame.", "Metal crack, rotors winding down, sudden birdsong.", "One strong cello statement with no percussion."),
            Beat("Hana's Truth", "Hana admits she designed the station protocol that abandoned the coast; she offers Ilyeon the compass and lets him decide whether to continue.", "Still, respectful eye-level two-shot with a very slow push.", "Soft water, cloth, distant thunder; dialogue intimate and dry.", "Barely audible sustained strings.", "Hana (S1): <d>[Korean] 우리 둘 다 명령을 따랐어. 이제 선택하자.</d>"),
            Beat("The Turning Boat", "Ilyeon returns the compass to Hana, turns the sail toward the dead tree, and takes the tiller as the wind sharply strengthens.", "Camera swings with the boom and settles forward over the bow.", "Rope tension, sail crack, water accelerating.", "Determined ostinato and low hand drum."),
            Beat("Sky Impact", "A massive greenhouse ring strikes the sea behind them, raising a wall of water that races toward the small boat.", "Very wide scale shot snaps into a fast stern-mounted pursuit view, no cut.", "Delayed orbital boom, metal roar, approaching wave.", "Orchestra climbs in a single unresolved surge."),
            Beat("Riding the Wall", "Ilyeon steers the boat up the face of the wave while Hana lashes herself to the mast and shields the seed compass inside her coat.", "Physical handheld boat camera pitches with the hull and maintains believable horizon motion.", "Wave thunder, hull strain, shouted breath, rigging.", "Percussion and brass peak without drowning physical sound."),
            Beat("Quiet Eye", "At the wave crest everything becomes briefly silent; the boat floats against the clouds and Hana sees green light waking beneath every water channel below.", "Slow-motion-like but real-time aerial orbit, droplets suspended only by the crest's motion.", "Sound narrows to heartbeat, wind, and a pure seed tone.", "Music drops to solo female voice without words."),
            Beat("Broken Ilyeon", "The boat crashes down; Ilyeon's shoulder tears open and reveals a reactor core whose pulse exactly matches the planetary roots.", "Tight aftermath tracking from damaged shoulder to compass to Hana's realization.", "Impact tail, sparking core, water slosh, matching dual pulses.", "Cello and synth motifs finally synchronize."),
            Beat("The Bridge", "Ilyeon explains that his core can restart the network only once; the dead tree opens a dark root doorway as they approach.", "Slow forward dolly between the pair toward the vast doorway.", "Core hum, roots grinding open, rain stops.", "Grave low choir and the main theme in minor.", "Ilyeon (S2): <d>[Korean] 심장은 원래 다리였어요.</d>"),
        ),
    ),
    (
        "The Root Cathedral",
        "act4-root-cathedral.png",
        (
            Beat("Threshold", "Hana and wounded Ilyeon enter the hollow tree, where old turbine blades and living roots form a cathedral nave around black water.", "One slow steadicam procession from behind, passing huge foreground roots.", "Footsteps in water, turbine creak, vast organic room tone.", "Low choir with long gaps of silence."),
            Beat("Root Memories", "Bioluminescent sap shows brief memories in the bark: forests, fires, evacuation, and the first moment Hana activated young Ilyeon.", "Camera glides close to the bark while reflections cross their faces.", "Layered whispers, leaves, archival alarms, sap crackle.", "Fragmented versions of the piano motif."),
            Beat("The Guardian", "A ring of corroded turbine blades spins awake around the altar, treating them as contaminants; Ilyeon shields Hana from the first sweep.", "Low action camera tracks laterally with strong blade parallax.", "Turbine startup, blade whoosh, metal impact, bark splinters.", "Tense asymmetric percussion."),
            Beat("Brass Compass Opens", "Hana transforms the compass into a botanical key; its rings unfold and project the original root code across the chamber.", "Macro mechanical orbit expands to a top-down view of the complete code pattern.", "Precise brass clicks, seed hum, machinery slowing.", "Celesta pattern grows into strings."),
            Beat("Denied", "The root altar rejects Hana's human authorization and pulls the seed back out of the soil; she refuses to force it and places her bare hand on the living root.", "Locked close shot emphasizes the physical contact and restrained reaction.", "Wet soil, root thud, Hana's breath, fading machinery.", "Music nearly silent, one bass harmonic."),
            Beat("Ilyeon Remembers", "Ilyeon recalls that the network was designed to accept a nonhuman bridge; he disconnects the reactor cable from his chest despite Hana's attempt to stop him.", "Slow half orbit around their opposing hands, ending on eye contact.", "Chest seals opening, cable tension, quiet rain from above.", "Main theme on solo cello.", "Hana (S1): <d>[Korean] 그러면 넌 사라져.</d> Ilyeon (S2): <d>[Korean] 아니요. 퍼지는 거예요.</d>"),
            Beat("Hold the Blades", "Ilyeon spreads both arms and physically holds apart collapsing turbine blades while luminous cracks race across his porcelain body.", "Soil-level wide lens pushes toward him through sparks and rain.", "Metal scream, servo strain, sparks, root heartbeat.", "Full orchestra builds slowly, no early peak."),
            Beat("Planting", "Hana kneels, presses the golden seed into black soil, and covers it with the clean earth gifted by the survivor; the first gold root reaches toward camera.", "Intimate macro-to-medium pullback centered on hands, seed, then faces.", "Soil grains, seed shell, heartbeat pulse, Hana crying silently.", "Wordless choir joins the cello."),
            Beat("Last Look", "Ilyeon and Hana exchange a final calm look as his body freezes into a white branching lattice; he manages one small human smile.", "Long-lens close reverse drift with shallow depth, no melodramatic shake.", "Turbines slowing, breath, delicate porcelain cracks.", "Piano motif resolves for the first time.", "Ilyeon (S2): <d>[Korean] 봄에는 깨워 주세요.</d>"),
            Beat("Ignition", "The golden root meets Ilyeon's core and a wave of light races through every root, cable, flooded tunnel, and tower foundation across the city.", "Rapid but continuous impossible-camera flight following the light from altar to city scale.", "Deep clean pulse, roots moving, water resonating, electrical systems waking.", "Orchestral and choral climax synchronized to the wave."),
            Beat("The Tree Breathes", "The dead tree draws one enormous breath of wind; buds erupt along its branches and the turbines turn gently with fresh green leaves.", "Epic exterior crane rises from dark doorway to the crown against clearing clouds.", "Massive bark movement, wind through new leaves, rain becoming soft.", "Climax releases into broad horn and strings."),
            Beat("Hana Alone", "At dawn Hana sits beside the motionless white lattice that was Ilyeon; a tiny green shoot emerges from his cracked palm and curls around her finger.", "Quiet eye-level dolly inward, ending on the living contact.", "Dripping cavern, birds returning outside, one tiny leaf unfurling.", "Solo piano and warm cello, very soft."),
        ),
    ),
    (
        "After the Rain",
        "act5-rewilded-city.png",
        (
            Beat("Water Falls", "Across the city, water levels begin receding through reopened channels while roots lift streets into terraces and fish follow the current.", "High aerial river-follow shot with real depth and changing scale.", "Rushing drainage, birds, distant masonry, no dialogue.", "Light rhythmic strings and wooden percussion."),
            Beat("Capsules Open", "The orbital seed capsules embedded across rooftops open together; shoots of many colors unfold in rain-washed sunlight.", "Macro seed opening transitions through continuous focus pulls to a citywide vista.", "Hundreds of soft clicks, leaves, insects awakening.", "Celesta and flute variations of the seed motif."),
            Beat("The Survivors", "Boats emerge from hidden apartments; people see green streets for the first time and help one another onto newly exposed ground.", "Human-height observational tracking, natural imperfect movement.", "Voices, oars, wet footsteps, relieved laughter.", "Warm ensemble strings kept restrained."),
            Beat("Hana Returns", "Hana brings a single golden leaf to the rooftop survivor and plants it in the jar now empty of soil; no words are needed.", "Simple medium two-shot with a slow rack focus to the leaf.", "Rooftop wind, ceramic jar, cloth movement.", "Solo viola and piano."),
            Beat("White Lattice", "Inside the tree, vines grow through Ilyeon's frozen lattice, replacing broken cables with living fibers; his fingers twitch once.", "Time-compressed organic macro motion resolves into real-time close-up.", "Fiber creaks, sap flow, faint servo restart.", "Low synth pulse returns beneath natural instruments."),
            Beat("First Step", "Ilyeon wakes, newly repaired by vines and white flowers, and takes an unsteady first step out of the root cathedral into sunlight.", "Backward low-angle dolly matched to each careful step.", "Porcelain joints, leaves brushing armor, birds and water.", "Main theme on cello, flute, and gentle drum."),
            Beat("Finding Hana", "Ilyeon follows paper birds children have tied along the flooded avenue and sees Hana working in a community garden ahead.", "Long telephoto reveal becomes a measured forward track.", "Paper flutter, gardening tools, children whispering.", "Piano motif brightens without swelling."),
            Beat("Reunion", "Hana turns, recognizes Ilyeon, and simply presses her forehead to his; flowers on his shoulder open in response.", "Close circular move at eye level, background falling softly away.", "Quiet breath, fabric and porcelain contact, small flower openings.", "Near-silence followed by one full warm chord.", "Hana (S1): <d>[Korean] 봄이야.</d>"),
            Beat("Years in One Garden", "A seasonal passage shows the same garden through spring rain, summer growth, autumn seed gathering, and winter lanterns while Hana and Ilyeon work side by side.", "Locked composition with motivated seasonal transformations and continuous hand action.", "Layered seasonal ambience connected by the same garden bell.", "Four subtle variations of the main theme."),
            Beat("The School of Seeds", "Years later, Hana teaches children to read seed patterns while Ilyeon repairs paper birds; the once-dead tree fills the window.", "Warm interior dolly between children's hands to the two leads.", "Pencils, paper folds, leaves outside, calm classroom murmur.", "Light chamber ensemble, playful but mature."),
            Beat("Paper Birds", "On a restored transit roof, the children release paper birds into the sunrise; living birds join them while Hana and Ilyeon watch beneath the golden tree.", "Wide crane rises with the paper birds and slowly pulls away.", "Paper wings, wind in leaves, children laughing, city water.", "Full main theme returns with restrained choir."),
            Beat("A Living Planet", "The camera continues pulling from the rooftop through the regenerated city, above the dead tree now green, through clouds, and into orbit where Earth glows with new river networks; one surviving greenhouse capsule sprouts against the window.", "One continuous final pullback from intimate human scale to planetary wide, ending completely stable.", "City ambience thins into wind, then quiet orbital hum and one seed chime.", "Main theme completes, orchestra fades to solo piano and silence."),
        ),
    ),
)


def all_shots() -> list[dict[str, Any]]:
    shots: list[dict[str, Any]] = []
    number = 0
    for act_no, (act_title, anchor, beats) in enumerate(ACTS, 1):
        if len(beats) != 12:
            raise ValueError(f"Act {act_no} must contain exactly 12 beats")
        for beat_no, beat in enumerate(beats, 1):
            number += 1
            hero = beat_no in {1, 6, 12}
            shots.append(
                {
                    "number": number,
                    "slug": f"{number:02d}_{re.sub(r'[^a-z0-9]+', '_', beat.title.lower()).strip('_')}",
                    "act": act_no,
                    "act_title": act_title,
                    "anchor": anchor,
                    "identity_anchor": "act1-orbital-greenhouse.png",
                    "beat": beat,
                    "turbo": not hero,
                    "steps": 4 if not hero else 25,
                    "ref_image_size": "match" if not hero else "max",
                    "seed": 219700000 + number,
                }
            )
    if len(shots) != 60:
        raise ValueError(f"Expected 60 shots, got {len(shots)}")
    return shots


def prompt_for(shot: dict[str, Any]) -> str:
    beat: Beat = shot["beat"]
    dialogue = (
        " During the middle phase, the referenced speakers deliver the following line with natural lip movement, complete mouth closure after each sentence, and restrained Korean dramatic delivery: "
        + beat.dialogue
        if beat.dialogue
        else " Neither character speaks; emotion is conveyed through precise gaze, breathing, posture, and hand movement."
    )
    return f"""subject_definitions:
<Subject 1> is Dr. Hana Seo, the elderly East Asian woman shown across <Picture 1> and <Picture 2>, with swept-back silver hair, a lined expressive face, a weathered indigo botanical utility coat, and small brass instruments. Her appearance, age, costume, and realistic human proportions must remain fully consistent.
<Subject 2> is Ilyeon, the teenage maintenance android shown across <Picture 1> and <Picture 2>, with a porcelain humanlike face, short dark hair, cracked ivory mechanical panels, visible precise joints, and living moss around his shoulder seams. His identity, face, materials, scale, and anatomy must remain fully consistent.
<Subject 3> is the tiny golden two-leaf seedling and its brass compass vessel shown in the references. It emits warm gold light but never becomes a weapon or a fantasy spell effect.
<Subject 4> is the act-specific science-fiction environment, material language, weather, and cinematic color palette established by <Picture 1>. <Picture 2> reinforces the lead characters' facial and costume identity.

summary:
[reference generation] Shot {shot['number']:02d}, “{beat.title},” is a fifteen-second continuous live-action science-fiction film shot in Act {shot['act']}, “{shot['act_title']}.” It preserves <Subject 1>, <Subject 2>, and <Subject 3> while using <Subject 4> as the production-design reference. The shot creates genuine articulated performance, physical interaction, environmental motion, synchronized Korean dialogue when specified, ambience, effects, and score.

retention_analysis:
<Subject 1> (appears throughout [Shot 1]): fully_preserved - Hana's elderly Korean identity, silver hair, indigo coat, brass tools, scale, and realistic facial detail remain stable while her pose and expression follow the new action.
<Subject 2> (appears throughout [Shot 1]): fully_preserved - Ilyeon's youthful porcelain face, dark hair, ivory mechanics, moss, scale, and articulated joints remain stable without morphing or human-skin replacement.
<Subject 3> (appears where described in [Shot 1]): fully_preserved - the two-leaf form, brass vessel, small scale, and warm-gold emission remain recognizable.
<Subject 4> (appears throughout [Shot 1]): partially_preserved - its grounded production design, physical materials, weather behavior, and teal-amber cinematic lighting are transferred to the new composition required by the story.

detailed_description:
The target video has premium live-action feature-film realism, restrained performances, physically plausible motion, detailed tactile surfaces, natural motion blur, stable faces and hands, deep foreground-to-background staging, and a subtle teal-and-amber grade. It contains no titles, captions, logos, watermarks, picture borders, frozen poses, Ken Burns motion, duplicated figures, extra limbs, plastic skin, abrupt identity changes, or decorative montage. [Shot 1] A single continuous shot begins in a fresh composition rather than copying the exact framing of either reference. {beat.action} {beat.camera} The lens behaves like real cinema glass: highlights bloom gently but retain texture, faces keep natural pores and fine lines, wet metal and porcelain reflect only motivated sources, and depth of field changes solely through physical focus pulls. Foreground objects cross the lens only when camera movement makes their travel plausible. Hana moves with the measured balance and economy of an elderly expert, while Ilyeon's movements combine youthful intention with precise mechanical joints; neither slides, floats, or changes scale. Their gazes meet the correct object or partner before each hand action, and every touch visibly transfers weight. Light direction, rain, water level, wind, reflections, damage, carried objects, and costume wetness remain continuous from the opening to the final frame. From 00:00.000 to 00:04.800, the shot clearly establishes geography, screen direction, the current physical task, and the relative positions of <Subject 1>, <Subject 2>, and <Subject 3>; secondary environment motion begins immediately and never freezes. From 00:04.800 to 00:10.200, the central action develops through complete body mechanics, clear cause and effect, believable weight, eye lines, contact shadows, cloth or panel response, and continuous background parallax.{dialogue} From 00:10.200 to 00:15.000, the action reaches one readable emotional or physical resolution and holds only long enough for the audience to understand it; hair, moss, cloth, water, rain, smoke, leaves, reflections, and practical lights continue moving naturally through the final frame. The camera move eases to a motivated endpoint without an artificial digital zoom. Preserve cinematic continuity, 180-degree screen direction, realistic scale, and a clean last frame suitable for editorial cutting.

overall_soundscape:
{beat.sound} All physical sounds have credible distance, perspective, reverberation, and synchronization. Dialogue remains intelligible without muting the environment; no generic trailer booms are added unless the described event physically creates one.

non_diegetic_music:
{beat.music} The score develops across the fifteen seconds, supports rather than replaces the scene, and ends with an edit-friendly musical tail."""


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def canonical_sha256(value: Any) -> str:
    encoded = json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def atomic_write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary: Path | None = None
    try:
        with tempfile.NamedTemporaryFile(
            "w", encoding="utf-8", dir=path.parent, prefix=f".{path.name}.", suffix=".tmp", delete=False,
        ) as stream:
            temporary = Path(stream.name)
            json.dump(value, stream, ensure_ascii=False, indent=2)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        if temporary is not None and temporary.exists():
            temporary.unlink()


def load_state(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {"version": STATE_VERSION, "jobs": {}, "completed": {}}
    try:
        state = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise RuntimeError(f"State file is unreadable; refusing to overwrite {path}: {error}") from error
    if not isinstance(state, dict) or not isinstance(state.get("jobs", {}), dict) or not isinstance(state.get("completed", {}), dict):
        raise RuntimeError(f"State file has an invalid shape; refusing to overwrite {path}")
    state["version"] = STATE_VERSION
    state.setdefault("jobs", {})
    state.setdefault("completed", {})
    return state


def completion_record(
    path: Path, prompt_id: str, fingerprints: dict[str, Any], probe: dict[str, Any],
) -> dict[str, Any]:
    resolved = path.resolve()
    return {
        "path": str(resolved),
        "sha256": sha256_file(resolved),
        "prompt_id": prompt_id,
        "fingerprints": fingerprints,
        "probe": probe,
    }


def validated_completion(entry: Any, fingerprints: dict[str, Any]) -> dict[str, Any] | None:
    if not isinstance(entry, dict) or entry.get("fingerprints") != fingerprints:
        return None
    path_value = entry.get("path")
    expected_sha256 = entry.get("sha256")
    if not isinstance(path_value, str) or not isinstance(expected_sha256, str):
        return None
    path = Path(path_value)
    try:
        if not path.is_file() or sha256_file(path) != expected_sha256:
            return None
        probe = verify_video(path)
    except (OSError, RuntimeError, subprocess.SubprocessError, json.JSONDecodeError):
        return None
    return entry | {"path": str(path.resolve()), "probe": probe}


def job_record(prompt_id: str, fingerprints: dict[str, Any]) -> dict[str, Any]:
    return {"prompt_id": prompt_id, "fingerprints": fingerprints}


def matching_prompt_id(entry: Any, fingerprints: dict[str, Any]) -> str | None:
    if not isinstance(entry, dict) or entry.get("fingerprints") != fingerprints:
        return None
    prompt_id = entry.get("prompt_id")
    return prompt_id if isinstance(prompt_id, str) and prompt_id else None


def fingerprints_for(
    template_sha256: str, workflow_path: Path, prompt: str, image_sha256: dict[str, str],
) -> dict[str, Any]:
    parts: dict[str, Any] = {
        "template_sha256": template_sha256,
        "workflow_sha256": sha256_file(workflow_path),
        "prompt_sha256": hashlib.sha256(prompt.encode("utf-8")).hexdigest(),
        "input_sha256": dict(sorted(image_sha256.items())),
    }
    parts["fingerprint"] = canonical_sha256(parts)
    return parts


def merge_manifest(path: Path, entries: list[dict[str, Any]]) -> None:
    existing: list[Any] = []
    if path.exists():
        try:
            existing = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as error:
            raise RuntimeError(f"Cannot safely merge existing manifest {path}: {error}") from error
        if not isinstance(existing, list):
            raise RuntimeError(f"Cannot safely merge non-list manifest {path}")

    by_number: dict[int, dict[str, Any]] = {}
    for entry in [*existing, *entries]:
        if not isinstance(entry, dict) or not isinstance(entry.get("number"), int):
            raise RuntimeError(f"Invalid manifest entry in {path}: {entry!r}")
        by_number[entry["number"]] = entry
    atomic_write_json(path, [by_number[number] for number in sorted(by_number)])


def validate_template_schema(template: dict[str, Any]) -> None:
    nodes = template.get("nodes")
    if not isinstance(nodes, list):
        raise RuntimeError("Ref2VA template has no UI node list")
    actual = {item.get("id"): item.get("type") for item in nodes if isinstance(item, dict)}
    mismatches = {
        node_id: {"expected": node_type, "actual": actual.get(node_id)}
        for node_id, node_type in EXPECTED_TEMPLATE_NODES.items() if actual.get(node_id) != node_type
    }
    if mismatches:
        raise RuntimeError(f"Ref2VA template node contract changed: {mismatches}")


def _require_executable(command: str, label: str) -> None:
    resolved = Path(command) if Path(command).is_absolute() else Path(shutil.which(command) or "")
    if not resolved.is_file() or not os.access(resolved, os.X_OK):
        raise RuntimeError(f"{label} executable is unavailable: {command}")


def preflight_local(root: Path, frame_dir: Path, required_images: list[str], template: dict[str, Any]) -> None:
    validate_template_schema(template)
    _require_executable(MCP_BIN, "Comfy MCP")
    _require_executable(COMFY_BIN, "comfy-cli")
    _require_executable("ffprobe", "ffprobe")
    if not frame_dir.is_dir():
        raise RuntimeError(f"Reference frame directory does not exist: {frame_dir}")
    if not os.access(root, os.W_OK):
        raise RuntimeError(f"Generation root is not writable: {root}")
    free = shutil.disk_usage(root).free
    if free < MIN_FREE_BYTES:
        raise RuntimeError(f"Generation root has less than 1 GiB free: {root} ({free} bytes)")
    for name in required_images:
        source = frame_dir / name
        if not source.is_file() or source.stat().st_size == 0:
            raise RuntimeError(f"Reference image is missing or empty: {source}")
        if source.suffix.lower() == ".png":
            with source.open("rb") as stream:
                if stream.read(8) != b"\x89PNG\r\n\x1a\n":
                    raise RuntimeError(f"Reference image is not a valid PNG file: {source}")


def download(url: str, destination: Path, expected_sha256: str | None = None) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    with urllib.request.urlopen(url, timeout=60) as response:
        data = response.read()
    if expected_sha256:
        actual = hashlib.sha256(data).hexdigest()
        if actual != expected_sha256:
            raise RuntimeError(f"SHA-256 mismatch for {url}: {actual}")
    temporary: Path | None = None
    try:
        with tempfile.NamedTemporaryFile("wb", dir=destination.parent, prefix=f".{destination.name}.", suffix=".tmp", delete=False) as stream:
            temporary = Path(stream.name)
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, destination)
    finally:
        if temporary is not None and temporary.exists():
            temporary.unlink()


def ensure_template(url: str, destination: Path, expected_sha256: str) -> None:
    if destination.is_file() and sha256_file(destination) == expected_sha256:
        return
    download(url, destination, expected_sha256)


def text_content(result: Any) -> str:
    return "\n".join(block.text for block in getattr(result, "content", ()) if hasattr(block, "text"))


def payload(result: Any) -> dict[str, Any]:
    is_error = getattr(result, "is_error", getattr(result, "isError", False))
    if is_error:
        raise RuntimeError(f"Comfy MCP tool failed: {text_content(result)}")
    for attr in ("structuredContent", "structured_content"):
        structured = getattr(result, attr, None)
        if isinstance(structured, dict):
            return structured
    raw = text_content(result).strip()
    try:
        parsed = json.loads(raw)
        return parsed if isinstance(parsed, dict) else {"value": parsed}
    except json.JSONDecodeError:
        match = re.search(r"\{.*\}", raw, re.DOTALL)
        if match:
            parsed = json.loads(match.group(0))
            if isinstance(parsed, dict):
                return parsed
    return {"text": raw}


def find_value(value: Any, key: str) -> Any:
    if isinstance(value, dict):
        if key in value:
            return value[key]
        for child in value.values():
            found = find_value(child, key)
            if found is not None:
                return found
    elif isinstance(value, list):
        for child in value:
            found = find_value(child, key)
            if found is not None:
                return found
    return None


def node(graph: dict[str, Any], node_id: int) -> dict[str, Any]:
    return next(item for item in graph["nodes"] if item["id"] == node_id)


def set_widgets(item: dict[str, Any], values: list[Any], named: dict[str, Any]) -> None:
    item["widgets_values"] = values
    item["widgets_values_named"] = named


def workflow_for(template: dict[str, Any], shot: dict[str, Any]) -> dict[str, Any]:
    graph = copy.deepcopy(template)
    prompt = prompt_for(shot)

    set_widgets(node(graph, 137), [shot["anchor"], "image"], {"image": shot["anchor"], "upload": "image"})
    set_widgets(node(graph, 139), [shot["identity_anchor"], "image"], {"image": shot["identity_anchor"], "upload": "image"})
    set_widgets(node(graph, 138), [prompt], {"value": prompt})
    set_widgets(
        node(graph, 136),
        [prompt, WIDTH, HEIGHT, LENGTH, shot["ref_image_size"]],
        {"prompt": prompt, "width": WIDTH, "height": HEIGHT, "length": LENGTH, "ref_image_size": shot["ref_image_size"]},
    )
    set_widgets(node(graph, 115), ["16:9 (Widescreen)", 0.98, 32], {"aspect_ratio": "16:9 (Widescreen)", "megapixels": 0.98, "multiple": 32})
    set_widgets(node(graph, 132), [SECONDS], {"value": SECONDS})
    set_widgets(node(graph, 129), [shot["seed"], "fixed"], {"noise_seed": shot["seed"], "control_after_generate": "fixed"})
    set_widgets(node(graph, 143), [shot["steps"], "fixed"], {"value": shot["steps"], "fixed": "fixed"})
    set_widgets(node(graph, 144), [4, "fixed"], {"value": 4, "fixed": "fixed"})
    set_widgets(node(graph, 145), [R2V_LORA, 1.0], {"lora_name": R2V_LORA, "strength_model": 1.0})
    set_widgets(node(graph, 146), [shot["turbo"]], {"value": shot["turbo"]})
    set_widgets(node(graph, 124), ["beta", 20, 1.0], {"scheduler": "beta", "steps": 20, "denoise": 1.0})
    prefix = f"video/last_gardener/{shot['slug']}"
    set_widgets(node(graph, 92), [prefix, "auto", "auto"], {"filename_prefix": prefix, "format": "auto", "codec": "auto"})
    return graph


def terminal_status(data: dict[str, Any]) -> str:
    status = find_value(data, "status")
    if isinstance(status, dict):
        status = status.get("status_str") or status.get("status")
    return str(status or "").lower()


class TerminalJobError(RuntimeError):
    """A job definitely reached a failure state, so resubmission is safe."""


class InvalidGeneratedOutput(RuntimeError):
    """A completed job produced a file that failed deterministic media QC."""


async def wait_for_job(session: ClientSession, prompt_id: str) -> dict[str, Any]:
    for _ in range(360):
        data = payload(await session.call_tool("job", {"action": "wait", "prompt_id": prompt_id, "timeout_seconds": 25}))
        status = terminal_status(data)
        if status in SUCCESS_STATUSES:
            return data
        if status in FAILURE_STATUSES:
            raise TerminalJobError(f"H3 job {prompt_id} ended as {status}: {data}")
        print("WAIT", prompt_id, status or "running", flush=True)
    raise TimeoutError(f"H3 job timed out: {prompt_id}")


async def resume_job(session: ClientSession, prompt_id: str) -> dict[str, Any]:
    data = payload(await session.call_tool("job", {"action": "status", "prompt_id": prompt_id}))
    status = terminal_status(data)
    if status in SUCCESS_STATUSES:
        return data
    if status in FAILURE_STATUSES:
        raise TerminalJobError(f"H3 job {prompt_id} ended as {status}: {data}")
    print("RESUME", prompt_id, status or "unknown", flush=True)
    return await wait_for_job(session, prompt_id)


def probe_video(path: Path) -> dict[str, Any]:
    command = [
        "ffprobe", "-v", "error", "-count_frames", "-show_entries",
        (
            "format=duration,size:stream="
            "index,codec_name,codec_type,width,height,r_frame_rate,avg_frame_rate,"
            "duration,nb_frames,nb_read_frames,sample_rate,channels"
        ),
        "-of", "json", str(path),
    ]
    return json.loads(subprocess.check_output(command, text=True))


def verify_video(path: Path) -> dict[str, Any]:
    data = probe_video(path)
    streams = data.get("streams", [])
    video = next((s for s in streams if s.get("codec_type") == "video"), None)
    audio = next((s for s in streams if s.get("codec_type") == "audio"), None)
    if not video or not audio:
        raise RuntimeError(f"Invalid H3 output {path}: {data}")

    try:
        format_duration = float(data["format"]["duration"])
        video_duration = float(video["duration"])
        audio_duration = float(audio["duration"])
        frame_count = int(video.get("nb_read_frames") or video.get("nb_frames"))
    except (KeyError, TypeError, ValueError) as error:
        raise RuntimeError(f"Incomplete H3 stream metadata for {path}: {data}") from error

    if min(format_duration, video_duration, audio_duration) < 14.8:
        raise RuntimeError(f"Short H3 stream in {path}: {data}")
    if abs(video_duration - audio_duration) > 0.25:
        raise RuntimeError(f"H3 audio/video duration mismatch for {path}: {data}")
    if frame_count != LENGTH:
        raise RuntimeError(f"H3 output must contain {LENGTH} decoded frames, got {frame_count}: {path}")
    if (
        video.get("codec_name"), video.get("width"), video.get("height"),
        video.get("r_frame_rate"), video.get("avg_frame_rate"),
    ) != ("h264", WIDTH, HEIGHT, "24/1", "24/1"):
        raise RuntimeError(f"Wrong H3 video format for {path}: {video}")
    if (
        audio.get("codec_name"), int(audio.get("sample_rate", 0)), int(audio.get("channels", 0)),
    ) != ("aac", 32000, 2):
        raise RuntimeError(f"Wrong H3 audio format for {path}: {audio}")
    return data


def snapshot_mp4(directory: Path) -> dict[Path, tuple[int, int]]:
    return {
        path.resolve(): (path.stat().st_mtime_ns, path.stat().st_size)
        for path in directory.rglob("*.mp4") if path.is_file()
    }


def _strings(value: Any) -> list[str]:
    if isinstance(value, str):
        return [value]
    if isinstance(value, dict):
        return [item for child in value.values() for item in _strings(child)]
    if isinstance(value, list):
        return [item for child in value for item in _strings(child)]
    return []


def select_fetched_output(
    fetched: dict[str, Any], fetched_dir: Path, slug: str, before: dict[Path, tuple[int, int]],
) -> Path:
    root = fetched_dir.resolve()
    reported: set[Path] = set()
    for value in _strings(fetched):
        if not value.lower().endswith(".mp4") or value.startswith(("http://", "https://")):
            continue
        raw = Path(value)
        candidates = [raw] if raw.is_absolute() else [root / raw, root / raw.name]
        candidates.extend(root.rglob(raw.name))
        for candidate in candidates:
            resolved = candidate.resolve()
            if resolved.is_relative_to(root) and resolved.is_file() and resolved.stat().st_size > 0:
                reported.add(resolved)

    exact = sorted(path for path in reported if slug in path.name)
    if len(exact) == 1:
        return exact[0]
    if len(exact) > 1:
        raise RuntimeError(f"fetch_outputs reported multiple MP4 files for {slug}: {exact}")

    changed = sorted(
        path.resolve() for path in fetched_dir.rglob("*.mp4")
        if path.is_file() and path.stat().st_size > 0 and slug in path.name
        and before.get(path.resolve()) != (path.stat().st_mtime_ns, path.stat().st_size)
    )
    if len(changed) == 1:
        return changed[0]
    if not changed:
        raise RuntimeError(f"fetch_outputs did not identify a new MP4 for {slug}: {fetched}")
    raise RuntimeError(f"Ambiguous new MP4 outputs for {slug}: {changed}; response={fetched}")


async def run(root: Path, frame_dir: Path, start: int, end: int) -> None:
    root.mkdir(parents=True, exist_ok=True)
    template_path = root / "video_minimax_h3_r2v.json"
    ensure_template(TEMPLATE_URL, template_path, TEMPLATE_SHA256)
    template = json.loads(template_path.read_text(encoding="utf-8"))

    shots = [s for s in all_shots() if start <= s["number"] <= end]
    required_images = sorted({s["anchor"] for s in shots} | {s["identity_anchor"] for s in shots})
    preflight_local(root, frame_dir, required_images, template)
    image_sha256 = {name: sha256_file(frame_dir / name) for name in required_images}
    for name in required_images:
        source = frame_dir / name
        destination = root / name
        if source.resolve() != destination.resolve():
            shutil.copyfile(source, destination)

    workflow_dir = root / "workflows"
    fetched_dir = root / "fetched"
    workflow_dir.mkdir(exist_ok=True)
    fetched_dir.mkdir(exist_ok=True)
    workflow_paths: dict[int, Path] = {}
    shot_fingerprints: dict[int, dict[str, Any]] = {}
    manifest = []
    for shot in shots:
        path = workflow_dir / f"{shot['slug']}.json"
        atomic_write_json(path, workflow_for(template, shot))
        workflow_paths[shot["number"]] = path
        prompt = prompt_for(shot)
        inputs = {name: image_sha256[name] for name in {shot["anchor"], shot["identity_anchor"]}}
        fingerprints = fingerprints_for(TEMPLATE_SHA256, path, prompt, inputs)
        shot_fingerprints[shot["number"]] = fingerprints
        manifest.append(
            {k: v for k, v in shot.items() if k != "beat"}
            | {"title": shot["beat"].title, "prompt": prompt, "fingerprints": fingerprints}
        )
    merge_manifest(root / "manifest.json", manifest)

    state_path = root / "state.json"
    state = load_state(state_path)

    env = os.environ.copy()
    env.update(COMFY_BIN=COMFY_BIN, COMFY_LOCAL_URL=COMFY_URL)
    env.pop("COMFYUI_URL", None)
    params = StdioServerParameters(command=MCP_BIN, args=[], env=env)

    async with stdio_client(params) as (read, write):
        async with ClientSession(read, write) as session:
            await session.initialize()
            tools = {tool.name for tool in (await session.list_tools()).tools}
            required = {"server_info", "upload_file", "validate_workflow", "run_workflow", "job", "fetch_outputs"}
            if missing := required - tools:
                raise RuntimeError(f"Missing Comfy MCP tools: {sorted(missing)}")
            info = payload(await session.call_tool("server_info", {}))
            if find_value(info, "running") is False:
                raise RuntimeError(f"ComfyUI is not running: {info}")
            print("SERVER_INFO", json.dumps(info, ensure_ascii=False)[:1000], flush=True)
            upload_paths = [str(root / name) for name in required_images]
            print("UPLOAD", json.dumps(payload(await session.call_tool("upload_file", {"paths": upload_paths, "overwrite": True})), ensure_ascii=False), flush=True)

            # Validate the entire selected range before the first expensive queue operation.
            for shot in shots:
                path = workflow_paths[shot["number"]]
                validation = payload(await session.call_tool("validate_workflow", {"workflow_path": str(path)}))
                if find_value(validation, "valid") is not True:
                    raise RuntimeError(f"Validation failed for {shot['slug']}: {validation}")
                print("VALID", shot["slug"], "turbo" if shot["turbo"] else "hero", flush=True)

            for shot in shots:
                slug = shot["slug"]
                fingerprints = shot_fingerprints[shot["number"]]
                prior = validated_completion(state["completed"].get(slug), fingerprints)
                if prior is not None:
                    state["completed"][slug] = prior
                    atomic_write_json(state_path, state)
                    print("SKIP_COMPLETED", slug, prior["path"], flush=True)
                    continue
                if slug in state["completed"]:
                    print("INVALIDATE_COMPLETED", slug, flush=True)
                    del state["completed"][slug]
                    atomic_write_json(state_path, state)

                path = workflow_paths[shot["number"]]
                stored_job = state["jobs"].get(slug)
                prompt_id = matching_prompt_id(stored_job, fingerprints)
                if stored_job is not None and prompt_id is None:
                    raise RuntimeError(
                        f"Untrusted or stale in-flight job for {slug}: {stored_job!r}. "
                        "Inspect/cancel that prompt before removing its state entry; automatic resubmission could duplicate GPU work."
                    )

                last_error: Exception | None = None
                for attempt in range(1, 4):
                    try:
                        if prompt_id is None:
                            submitted = payload(await session.call_tool("run_workflow", {"workflow_path": str(path), "wait": False}))
                            prompt_id = find_value(submitted, "prompt_id")
                            if not isinstance(prompt_id, str) or not prompt_id:
                                raise RuntimeError(f"prompt_id missing: {submitted}")
                            state["jobs"][slug] = job_record(prompt_id, fingerprints)
                            atomic_write_json(state_path, state)
                            print("QUEUED", slug, prompt_id, "attempt", attempt, flush=True)

                        await resume_job(session, prompt_id)
                        before = snapshot_mp4(fetched_dir)
                        fetched = payload(await session.call_tool("fetch_outputs", {"prompt_id": prompt_id, "out_dir": str(fetched_dir)}))
                        output = select_fetched_output(fetched, fetched_dir, slug, before)
                        try:
                            probe = verify_video(output)
                        except (RuntimeError, subprocess.SubprocessError, json.JSONDecodeError) as error:
                            raise InvalidGeneratedOutput(f"Completed job {prompt_id} produced invalid output {output}: {error}") from error
                        state["completed"][slug] = completion_record(output, prompt_id, fingerprints, probe)
                        state["jobs"].pop(slug, None)
                        atomic_write_json(state_path, state)
                        print("VERIFIED", slug, output, json.dumps(probe), flush=True)
                        last_error = None
                        break
                    except TerminalJobError as error:
                        # A terminal failure cannot still consume GPU work, so a new submit is safe.
                        last_error = error
                        state["jobs"].pop(slug, None)
                        atomic_write_json(state_path, state)
                        prompt_id = None
                        print("TERMINAL_RETRY", slug, attempt, repr(error), flush=True)
                    except InvalidGeneratedOutput as error:
                        # The prompt is completed, so replacing its bad output cannot duplicate live GPU work.
                        last_error = error
                        state["jobs"].pop(slug, None)
                        atomic_write_json(state_path, state)
                        prompt_id = None
                        print("OUTPUT_RETRY", slug, attempt, repr(error), flush=True)
                    except Exception as error:  # retry slow GPU and transient MCP failures
                        last_error = error
                        # Keep prompt_id: status/fetch failures are not proof that the GPU job stopped.
                        print("RESUME_RETRY", slug, attempt, prompt_id, repr(error), flush=True)
                    if attempt < 3:
                        await asyncio.sleep(5)
                if last_error:
                    raise last_error

    print("LAST_GARDENER_GENERATION_COMPLETE", root, flush=True)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=DEFAULT_ROOT)
    parser.add_argument("--frame-dir", type=Path, required=True)
    parser.add_argument("--start", type=int, default=1)
    parser.add_argument("--end", type=int, default=60)
    args = parser.parse_args()
    if not (1 <= args.start <= args.end <= 60):
        parser.error("shot range must satisfy 1 <= start <= end <= 60")
    asyncio.run(run(args.root, args.frame_dir, args.start, args.end))


if __name__ == "__main__":
    main()
