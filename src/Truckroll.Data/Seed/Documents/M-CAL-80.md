---
code: M-CAL-80
title: Calderon CAL-80 Series Gas Furnace, Service Manual
kind: Manual
applies_to: CAL-80
---

# Calderon CAL-80 Series Gas Furnace

Mid-efficiency (80% AFUE), single-stage, induced-draft gas furnace. Sizes CAL-80-60, CAL-80-80
and CAL-80-100 (input in thousands of BTU/h). Venting is Category I, through a metal chimney or
B-vent. Gas work requires a G2 licence or higher.

## Specifications

| Model       | Input (BTU/h) | Output (BTU/h) | Blower | Filter size  |
| ----------- | ------------- | -------------- | ------ | ------------ |
| CAL-80-60   | 60,000        | 48,000         | 1/3 hp | 16 x 25 x 1  |
| CAL-80-80   | 80,000        | 64,000         | 1/2 hp | 16 x 25 x 1  |
| CAL-80-100  | 100,000       | 80,000         | 3/4 hp | 20 x 25 x 1  |

Ignition is a silicon carbide hot surface ignitor (part IGN-80H), proven by a flame sensor rod.

## Status light codes

The control board LED flashes a code, pauses, and repeats.

| Flashes      | Meaning                                         | First check                                    |
| ------------ | ----------------------------------------------- | ---------------------------------------------- |
| Steady on    | Normal operation                                |                                                |
| 1            | Ignition lockout after three failed tries        | Ignitor, flame sensor, gas supply              |
| 2            | Pressure switch stuck closed                    | Switch contacts, wiring                        |
| 3            | Pressure switch did not close                   | Tubing, inducer, blocked vent                  |
| 4            | High limit open                                 | Dirty filter, closed registers, blower         |
| 5            | Flame sensed with the gas valve off              | Gas valve; shut gas off and call it in         |
| 6            | Reversed polarity or poor ground                | Line and neutral at the junction box           |

## Troubleshooting

### Blower runs but burners never light

Check the ignitor glows orange within 30 seconds of the inducer starting. If it doesn't, measure
its resistance cold: 40 to 90 ohms is normal, open circuit means it's cracked. Replace with
IGN-80H only; never touch the element with bare fingers. Units in the serial range listed in
service bulletin SB-2024-07 are prone to repeat ignitor cracking.

### Burners light then shut off after a few seconds

Almost always the flame sensor. Clean the rod with fine emery cloth and confirm at least
2 microamps DC in operation. Also confirm the furnace is properly grounded (code 6).

### Short cycling on the high limit

Replace the filter, open all supply registers and check the temperature rise is within 35 to
65 °F. Persistent high-limit trips with a clean filter point to undersized ductwork.

## Maintenance

Every heating season: replace the filter, clean the flame sensor, inspect the ignitor for
cracks, check the inducer for bearing noise, verify manifold pressure (3.5 in. w.c. natural gas),
test the high limit and pressure switch, and check for carbon monoxide at the flue.

## Parts

| Part             | Number     |
| ---------------- | ---------- |
| Hot surface ignitor | IGN-80H |
| Flame sensor     | FS-80      |
| Pressure switch  | PS-80-0.55 |
| Control board    | CB-80-2    |
| Inducer assembly | IND-80     |
