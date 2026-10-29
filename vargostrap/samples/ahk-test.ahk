; SPDX-FileCopyrightText: 2026 Vargostrap
;
; SPDX-License-Identifier: MPL-2.0
;
; Harmless test for Vargostrap's AutoHotkey integration. It only shows a tooltip in the top-left corner.
; No hotkeys and no input automation. Add it on the Bootstrapper page, launch Roblox through Vargostrap,
; check the tooltip appears, then close Roblox and check it disappears.

#Requires AutoHotkey v2.0
#SingleInstance Force
Persistent

ToolTip "Vargostrap AHK test script is running.`nIt should close when Roblox closes.", 10, 10
