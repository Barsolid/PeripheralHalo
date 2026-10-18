# Third-party notices

PeripheralHalo is distributed under GPL-2.0-only (see LICENSE).

## OpenRazer

Source: https://github.com/openrazer/openrazer

Wireless mouse USB product IDs, transaction ID mappings and battery query formats were adapted from `driver/razermouse_driver.h`, `driver/razermouse_driver.c`, and `driver/razerchromacommon.c` (GPL-2.0-or-later).

Copyright (c) 2015 Terri Cain <terri@dolphincorp.co.uk> and OpenRazer contributors. Original attribution and licenses: https://github.com/openrazer/openrazer/tree/master/LICENSES

## Solaar and Logitech

HID++ packet descriptions: Logitech's published HID++ documentation, https://github.com/Logitech/cpg-docs/tree/master/hidpp20 .

Battery interpretation and the millivolt interpolation reference points follow Solaar's `hidpp20.py`, https://github.com/pwr-Solaar/Solaar (GPL-2.0). Copyright Solaar contributors. The implementation in this repository was written for PeripheralHalo; the protocol references are recorded in docs/protocols.md.

## GearHub

The Windows HFP battery property identification and service discovery strategy were adapted from GearHub's BluetoothHfpBatteryProvider. Source: https://github.com/jenesei-software/gearhub . GearHub is MIT licensed; its copyright and full permission notice follow.

MIT License

Copyright (c) 2026 Cyril Strone

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
