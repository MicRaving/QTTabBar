﻿//    This file is part of QTTabBar, a shell extension for Microsoft
//    Windows Explorer.
//    Copyright (C) 2007-2010  Quizo, Paul Accisano
//
//    QTTabBar is free software: you can redistribute it and/or modify
//    it under the terms of the GNU General Public License as published by
//    the Free Software Foundation, either version 3 of the License, or
//    (at your option) any later version.
//
//    QTTabBar is distributed in the hope that it will be useful,
//    but WITHOUT ANY WARRANTY; without even the implied warranty of
//    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//    GNU General Public License for more details.
//
//    You should have received a copy of the GNU General Public License
//    along with QTTabBar.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using QTTabBarLib.Interop;

namespace QTTabBarLib {
    class ExtendedSysListView32 : ExtendedListViewCommon {

        private NativeWindowController EditController;
        private List<int> lstColumnFMT;
        private bool fListViewHasFocus;
        private int iListViewItemState;


        internal ExtendedSysListView32(ShellBrowserEx ShellBrowser, IntPtr hwndShellView, IntPtr hwndListView, IntPtr hwndSubDirTipMessageReflect)
                : base(ShellBrowser, hwndShellView, hwndListView, hwndSubDirTipMessageReflect) {
            SetStyleFlags();
        }

        private bool EditController_MessageCaptured(ref Message msg) {
            if(msg.Msg == 0xb1 /* EM_SETSEL */ && msg.WParam.ToInt32() != -1) {
                msg.LParam = EditController.OptionalHandle;
                EditController.MessageCaptured -= EditController_MessageCaptured;

                // This point, we could just call EditController.ReleaseHandle(),
                // but doing so here seems to cause strange effects on XP.  Ah
                // well, this is good enough.
            }
            return false;
        }

        protected override bool OnShellViewNotify(NMHDR nmhdr, ref Message msg) {
            // Process WM.NOTIFY.  These are all notifications from the 
            // SysListView32 control.  We will not get ANY of these on 
            // Windows 7, which means every single one of them has to 
            // have an alternative somewhere for the ItemsView control,
            // or it's not going to happen.
            switch(nmhdr.code) {
                case -12: // NM_CUSTOMDRAW
                    // This is for drawing alternating row colors.  I doubt
                    // very much we'll find an alternative for this...
                    return HandleLVCUSTOMDRAW(ref msg);

                case LVN.ITEMCHANGED: {
                        // There are three things happening here.
                        // 1. Notify plugins of selection changing: Handled through 
                        //    undocumented WM_USER+163 message
                        // 2. Redraw for Full Row Select: Not happening
                        // 3. Set new item DropHilighted: Handled through 
                        //    undocumented WM_USER+209 message

                    /*
                        // TODO
                     
                        IntPtr ptr;
                        if(QTUtility.instanceManager.TryGetButtonBarHandle(this.hwndExplorer, out ptr)) {
                            QTUtility2.SendCOPYDATASTRUCT(ptr, (IntPtr)13, null, (IntPtr)GetItemCount());
                        }
                     */
                        bool flag = QTUtility.IsVista && QTUtility.CheckConfig(Settings.ToggleFullRowSelect);
                        NMLISTVIEW nmlistview2 = (NMLISTVIEW)Marshal.PtrToStructure(msg.LParam, typeof(NMLISTVIEW));
                        if(nmlistview2.uChanged == 8 /*LVIF_STATE*/) {
                            uint num5 = nmlistview2.uNewState & LVIS.SELECTED;
                            uint num6 = nmlistview2.uOldState & LVIS.SELECTED;
                            uint num7 = nmlistview2.uNewState & LVIS.DROPHILITED;
                            uint num8 = nmlistview2.uOldState & LVIS.DROPHILITED;
                            uint num9 = nmlistview2.uNewState & LVIS.CUT;
                            uint num10 = nmlistview2.uOldState & LVIS.CUT;
                            if((num8 != num7)) {
                                if(num7 == 0) {
                                    OnDropHilighted(-1);
                                }
                                else {
                                    OnDropHilighted(nmlistview2.iItem);
                                }
                            }
                            if(flag) {
                                if(nmlistview2.iItem != -1 && ((num5 != num6) || (num7 != num8) || (num9 != num10)) && ViewMode == FVM.DETAILS) {
                                    PInvoke.SendMessage(nmlistview2.hdr.hwndFrom, LVM.REDRAWITEMS, (IntPtr)nmlistview2.iItem, (IntPtr)nmlistview2.iItem);
                                }
                            }
                            if(num5 != num6) {
                                OnSelectionChanged();
                            }
                        }
                        break;
                    }

                case LVN.INSERTITEM:
                case LVN.DELETEITEM:
                case LVN.DELETEALLITEMS:
                    // Handled through undocumented WM_USER+174 message
                    if(!QTUtility.CheckConfig(Settings.NoShowSubDirTips)) {
                        HideSubDirTip(1);
                    }
                    if(QTUtility.CheckConfig(Settings.AlternateRowColors) && (ViewMode == FVM.DETAILS)) {
                        PInvoke.InvalidateRect(nmhdr.hwndFrom, IntPtr.Zero, true);
                    }
                    ShellViewController.DefWndProc(ref msg);
                    OnItemCountChanged();
                    return true;

                case LVN.BEGINDRAG:
                    // This won't be necessary it seems.  On Windows 7, when you
                    // start to drag, a MOUSELEAVE message is sent, which hides
                    // the SubDirTip anyway.
                    ShellViewController.DefWndProc(ref msg);
                    HideSubDirTip(0xff);
                    break;

                case LVN.ITEMACTIVATE: {
                    // Handled by catching Double Clicks and Enter keys.  Ugh...
                    NMITEMACTIVATE nmitemactivate = (NMITEMACTIVATE)Marshal.PtrToStructure(msg.LParam, typeof(NMITEMACTIVATE));
                    Keys modKeys =
                        (((nmitemactivate.uKeyFlags & 1) == 1) ? Keys.Alt : Keys.None) |
                        (((nmitemactivate.uKeyFlags & 2) == 2) ? Keys.Control : Keys.None) |
                        (((nmitemactivate.uKeyFlags & 4) == 4) ? Keys.Shift : Keys.None);
                    if(OnItemActivated(modKeys)) return true;
                    break;
                }

                case LVN.ODSTATECHANGED:
                    // FullRowSelect doesn't look possible anyway, so whatever.
                    if(QTUtility.IsVista && QTUtility.CheckConfig(Settings.ToggleFullRowSelect)) {
                        NMLVODSTATECHANGE nmlvodstatechange = (NMLVODSTATECHANGE)Marshal.PtrToStructure(msg.LParam, typeof(NMLVODSTATECHANGE));
                        if(((nmlvodstatechange.uNewState & 2) == 2) && (ViewMode == FVM.DETAILS)) {
                            PInvoke.SendMessage(nmlvodstatechange.hdr.hwndFrom, LVM.REDRAWITEMS, (IntPtr)nmlvodstatechange.iFrom, (IntPtr)nmlvodstatechange.iTo);
                        }
                    }
                    break;

                case LVN.HOTTRACK:
                    // Handled through WM_MOUSEMOVE.
                    if(QTUtility.CheckConfig(Settings.ShowTooltipPreviews) || !QTUtility.CheckConfig(Settings.NoShowSubDirTips)) {
                        NMLISTVIEW nmlistview = (NMLISTVIEW)Marshal.PtrToStructure(msg.LParam, typeof(NMLISTVIEW));
                        OnHotTrack(nmlistview.iItem);
                    }
                    break;

                case LVN.KEYDOWN: {
                    // Handled through WM_KEYDOWN.
                    NMLVKEYDOWN nmlvkeydown = (NMLVKEYDOWN)Marshal.PtrToStructure(msg.LParam, typeof(NMLVKEYDOWN));
                    if(OnKeyDown((Keys)nmlvkeydown.wVKey)) {
                        msg.Result = (IntPtr)1;
                        return true;
                    }
                    else {
                        return false;
                    }                        
                }
                    
                case LVN.GETINFOTIP: {
                    // Handled through WM_NOTIFY / TTN_NEEDTEXT
                    NMLVGETINFOTIP nmlvgetinfotip = (NMLVGETINFOTIP)Marshal.PtrToStructure(msg.LParam, typeof(NMLVGETINFOTIP));
                    return OnGetInfoTip(nmlvgetinfotip.iItem, GetHotItem() != nmlvgetinfotip.iItem); // TODO there's got to be a better way.
                }

                case LVN.BEGINLABELEDIT:
                    // This is just for file renaming, which there's no need to
                    // mess with in Windows 7.
                    ShellViewController.DefWndProc(ref msg);
                    if(!QTUtility.IsVista && !QTUtility.CheckConfig(Settings.ExtWhileRenaming)) {
                        NMLVDISPINFO nmlvdispinfo = (NMLVDISPINFO)Marshal.PtrToStructure(msg.LParam, typeof(NMLVDISPINFO));
                        if(nmlvdispinfo.item.lParam != IntPtr.Zero) {
                            using(IDLWrapper idl = ShellBrowser.ILAppend(nmlvdispinfo.item.lParam)) {
                                OnFileRename(idl);
                            }
                        }
                    }
                    break;

                case LVN.ENDLABELEDIT: {
                    NMLVDISPINFO nmlvdispinfo2 = (NMLVDISPINFO)Marshal.PtrToStructure(msg.LParam, typeof(NMLVDISPINFO));
                    OnEndLabelEdit(nmlvdispinfo2.item);
                    break;
                }
            }
            return false;
        }

        private void SetStyleFlags() {
            if(ViewMode != FVM.DETAILS) return;
            uint flags = 0;
            if(QTUtility.CheckConfig(Settings.DetailsGridLines)) {
                flags |= LVS_EX.GRIDLINES;
            }
            else {
                flags &= ~LVS_EX.GRIDLINES;
            }
            if(QTUtility.CheckConfig(Settings.ToggleFullRowSelect) ^ QTUtility.IsVista) {
                flags |= LVS_EX.FULLROWSELECT;
            }
            else {
                flags &= ~LVS_EX.FULLROWSELECT;
            }
            const uint mask = LVS_EX.GRIDLINES | LVS_EX.FULLROWSELECT;
            PInvoke.SendMessage(Handle, LVM.SETEXTENDEDLISTVIEWSTYLE, (IntPtr)mask, (IntPtr)flags);
        }

        public override IntPtr GetEditControl() {
            return PInvoke.SendMessage(Handle, LVM.GETEDITCONTROL, IntPtr.Zero, IntPtr.Zero);
        }

        public override int GetFocusedItem() {
            if(HasFocus()) {
                int count = GetItemCount();
                for(int i = 0; i < count; ++i) {
                    int state = (int)PInvoke.SendMessage(ListViewController.Handle, LVM.GETITEMSTATE, (IntPtr)i, (IntPtr)LVIS.FOCUSED);
                    if(state != 0) {
                        return i;
                    }
                }
            }
            return -1;
        }

        public override Rectangle GetFocusedItemRect() {
            if(HasFocus()) {
                return GetLVITEMRECT(Handle, GetFocusedItem(), false, ViewMode).ToRectangle();
            }
            return new Rectangle(0, 0, 0, 0);
        }

        public override int GetItemCount() {
            return (int)PInvoke.SendMessage(Handle, LVM.GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
        }

        public override int GetSelectedCount() {
            return (int)PInvoke.SendMessage(Handle, LVM.GETSELECTEDCOUNT, IntPtr.Zero, IntPtr.Zero);
        }

        public override Point GetSubDirTipPoint(bool fByKey) {
            int iItem = fByKey ? GetFocusedItem() : GetHotItem();
            RECT rect = GetLVITEMRECT(ListViewController.Handle, iItem, true, ViewMode);
            return new Point(rect.right - 16, rect.bottom - 16);

        }

        private bool HandleLVCUSTOMDRAW(ref Message msg) {
            // TODO this needs to be cleaned
            if(QTUtility.CheckConfig(Settings.AlternateRowColors) && (ViewMode == FVM.DETAILS)) {
                NMLVCUSTOMDRAW structure = (NMLVCUSTOMDRAW)Marshal.PtrToStructure(msg.LParam, typeof(NMLVCUSTOMDRAW));
                int dwItemSpec = 0;
                if((ulong)structure.nmcd.dwItemSpec < Int32.MaxValue) {
                    dwItemSpec = (int)structure.nmcd.dwItemSpec;
                }
                switch(structure.nmcd.dwDrawStage) {
                    case CDDS.SUBITEM | CDDS.ITEMPREPAINT:
                        iListViewItemState = (int)PInvoke.SendMessage(
                                ListViewController.Handle, LVM.GETITEMSTATE, structure.nmcd.dwItemSpec,
                                (IntPtr)(LVIS.FOCUSED | LVIS.SELECTED | LVIS.DROPHILITED));

                        if(QTUtility.IsVista) {
                            int num4 = lstColumnFMT[structure.iSubItem];
                            structure.clrTextBk = QTUtility.ShellViewRowCOLORREF_Background;
                            structure.clrText = QTUtility.ShellViewRowCOLORREF_Text;
                            Marshal.StructureToPtr(structure, msg.LParam, false);
                            bool drawingHotItem = (dwItemSpec == GetHotItem());
                            bool fullRowSel = !QTUtility.CheckConfig(Settings.ToggleFullRowSelect);

                            msg.Result = (IntPtr)(CDRF.NEWFONT);
                            if(structure.iSubItem == 0 && !drawingHotItem) {
                                if(iListViewItemState == 0 && (num4 & 0x600) != 0) {
                                    msg.Result = (IntPtr)(CDRF.NEWFONT | CDRF.NOTIFYPOSTPAINT);
                                }
                                else if(iListViewItemState == LVIS.FOCUSED && !fullRowSel) {
                                    msg.Result = (IntPtr)(CDRF.NEWFONT | CDRF.NOTIFYPOSTPAINT);
                                }
                            }

                            if(structure.iSubItem > 0 && (!fullRowSel || !drawingHotItem)) {
                                if(!fullRowSel || (iListViewItemState & (LVIS.SELECTED | LVIS.DROPHILITED)) == 0) {
                                    using(Graphics graphics = Graphics.FromHdc(structure.nmcd.hdc)) {
                                        if(QTUtility.sbAlternate == null) {
                                            QTUtility.sbAlternate = new SolidBrush(QTUtility2.MakeColor(QTUtility.ShellViewRowCOLORREF_Background));
                                        }
                                        graphics.FillRectangle(QTUtility.sbAlternate, structure.nmcd.rc.ToRectangle());
                                    }
                                }
                            }
                        }
                        else {
                            msg.Result = (IntPtr)(CDRF.NOTIFYPOSTPAINT);
                        }
                        return true;

                    case CDDS.SUBITEM | CDDS.ITEMPOSTPAINT: {
                            RECT rc = structure.nmcd.rc;
                            if(!QTUtility.IsVista) {
                                rc = PInvoke.ListView_GetItemRect(ListViewController.Handle, dwItemSpec, structure.iSubItem, 2);
                            }
                            else {
                                rc.left += 0x10;
                            }
                            bool flag3 = false;
                            bool flag4 = false;
                            bool flag5 = QTUtility.CheckConfig(Settings.DetailsGridLines);
                            bool flag6 = QTUtility.CheckConfig(Settings.ToggleFullRowSelect) ^ QTUtility.IsVista;
                            bool flag7 = false;
                            if(!QTUtility.IsVista && QTUtility.fSingleClick) {
                                flag7 = (dwItemSpec == GetHotItem());
                            }
                            LVITEM lvitem = new LVITEM();
                            lvitem.pszText = Marshal.AllocHGlobal(520);
                            lvitem.cchTextMax = 260;
                            lvitem.iSubItem = structure.iSubItem;
                            lvitem.iItem = dwItemSpec;
                            lvitem.mask = 1;
                            IntPtr ptr3 = Marshal.AllocHGlobal(Marshal.SizeOf(lvitem));
                            Marshal.StructureToPtr(lvitem, ptr3, false);
                            PInvoke.SendMessage(ListViewController.Handle, LVM.GETITEM, IntPtr.Zero, ptr3);
                            if(QTUtility.sbAlternate == null) {
                                QTUtility.sbAlternate = new SolidBrush(QTUtility2.MakeColor(QTUtility.ShellViewRowCOLORREF_Background));
                            }
                            using(Graphics graphics2 = Graphics.FromHdc(structure.nmcd.hdc)) {
                                Rectangle rect = rc.ToRectangle();
                                if(flag5) {
                                    rect = new Rectangle(rc.left + 1, rc.top, rc.Width - 1, rc.Height - 1);
                                }
                                graphics2.FillRectangle(QTUtility.sbAlternate, rect);
                                if(!QTUtility.IsVista && ((structure.iSubItem == 0) || flag6)) {
                                    flag4 = (iListViewItemState & 8) == 8;
                                    if((iListViewItemState != 0) && (((iListViewItemState == 1) && fListViewHasFocus) || (iListViewItemState != 1))) {
                                        int width;
                                        if(flag6) {
                                            width = rc.Width;
                                        }
                                        else {
                                            width = 8 + ((int)PInvoke.SendMessage(ListViewController.Handle, LVM.GETSTRINGWIDTH, IntPtr.Zero, lvitem.pszText));
                                            if(width > rc.Width) {
                                                width = rc.Width;
                                            }
                                        }
                                        Rectangle rectangle2 = new Rectangle(rc.left, rc.top, width, flag5 ? (rc.Height - 1) : rc.Height);
                                        if(((iListViewItemState & 2) == 2) || flag4) {
                                            if(flag4) {
                                                graphics2.FillRectangle(SystemBrushes.Highlight, rectangle2);
                                            }
                                            else if(QTUtility.fSingleClick && flag7) {
                                                graphics2.FillRectangle(fListViewHasFocus ? SystemBrushes.HotTrack : SystemBrushes.Control, rectangle2);
                                            }
                                            else {
                                                graphics2.FillRectangle(fListViewHasFocus ? SystemBrushes.Highlight : SystemBrushes.Control, rectangle2);
                                            }
                                            flag3 = true;
                                        }
                                        if((fListViewHasFocus && ((iListViewItemState & 1) == 1)) && !flag6) {
                                            ControlPaint.DrawFocusRectangle(graphics2, rectangle2);
                                        }
                                    }
                                }
                                if(QTUtility.IsVista && ((iListViewItemState & 1) == 1)) {
                                    int num6 = rc.Width;
                                    if(!flag6) {
                                        num6 = 4 + ((int)PInvoke.SendMessage(ListViewController.Handle, LVM.GETSTRINGWIDTH, IntPtr.Zero, lvitem.pszText));
                                        if(num6 > rc.Width) {
                                            num6 = rc.Width;
                                        }
                                    }
                                    Rectangle rectangle = new Rectangle(rc.left + 1, rc.top + 1, num6, flag5 ? (rc.Height - 2) : (rc.Height - 1));
                                    ControlPaint.DrawFocusRectangle(graphics2, rectangle);
                                }
                            }
                            IntPtr zero = IntPtr.Zero;
                            IntPtr hgdiobj = IntPtr.Zero;
                            if(!QTUtility.IsVista && QTUtility.fSingleClick) {
                                LOGFONT logfont;
                                zero = PInvoke.GetCurrentObject(structure.nmcd.hdc, 6);
                                PInvoke.GetObject(zero, Marshal.SizeOf(typeof(LOGFONT)), out logfont);
                                if((structure.iSubItem == 0) || flag6) {
                                    logfont.lfUnderline = ((QTUtility.iIconUnderLineVal == 3) || flag7) ? ((byte)1) : ((byte)0);
                                }
                                else {
                                    logfont.lfUnderline = 0;
                                }
                                hgdiobj = PInvoke.CreateFontIndirect(ref logfont);
                                PInvoke.SelectObject(structure.nmcd.hdc, hgdiobj);
                            }
                            PInvoke.SetBkMode(structure.nmcd.hdc, 1);
                            int dwDTFormat = 0x8824;
                            if(QTUtility.IsRTL ? ((lstColumnFMT[structure.iSubItem] & 1) == 0) : ((lstColumnFMT[structure.iSubItem] & 1) == 1)) {
                                if(QTUtility.IsRTL) {
                                    dwDTFormat &= -3;
                                }
                                else {
                                    dwDTFormat |= 2;
                                }
                                rc.right -= 6;
                            }
                            else if(structure.iSubItem == 0) {
                                rc.left += 2;
                                rc.right -= 2;
                            }
                            else {
                                rc.left += 6;
                            }
                            if(flag3) {
                                PInvoke.SetTextColor(structure.nmcd.hdc, QTUtility2.MakeCOLORREF((fListViewHasFocus || flag4) ? SystemColors.HighlightText : SystemColors.WindowText));
                            }
                            else {
                                PInvoke.SetTextColor(structure.nmcd.hdc, QTUtility.ShellViewRowCOLORREF_Text);
                            }
                            PInvoke.DrawTextExW(structure.nmcd.hdc, lvitem.pszText, -1, ref rc, dwDTFormat, IntPtr.Zero);
                            Marshal.FreeHGlobal(lvitem.pszText);
                            Marshal.FreeHGlobal(ptr3);
                            msg.Result = IntPtr.Zero;
                            if(zero != IntPtr.Zero) {
                                PInvoke.SelectObject(structure.nmcd.hdc, zero);
                            }
                            if(hgdiobj != IntPtr.Zero) {
                                PInvoke.DeleteObject(hgdiobj);
                            }
                            return true;
                        }
                    case CDDS.ITEMPREPAINT:
                        if((dwItemSpec % 2) == 0) {
                            msg.Result = (IntPtr)0x20;
                            return true;
                        }
                        msg.Result = IntPtr.Zero;
                        return false;

                    case CDDS.PREPAINT: {
                            HDITEM hditem = new HDITEM();
                            hditem.mask = 4;
                            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(hditem));
                            Marshal.StructureToPtr(hditem, ptr, false);
                            IntPtr hWnd = PInvoke.SendMessage(ListViewController.Handle, LVM.GETHEADER, IntPtr.Zero, IntPtr.Zero);
                            int num2 = (int)PInvoke.SendMessage(hWnd, 0x1200, IntPtr.Zero, IntPtr.Zero);
                            if(lstColumnFMT == null) {
                                lstColumnFMT = new List<int>();
                            }
                            else {
                                lstColumnFMT.Clear();
                            }
                            for(int i = 0; i < num2; i++) {
                                PInvoke.SendMessage(hWnd, 0x120b, (IntPtr)i, ptr);
                                hditem = (HDITEM)Marshal.PtrToStructure(ptr, typeof(HDITEM));
                                lstColumnFMT.Add(hditem.fmt);
                            }
                            Marshal.FreeHGlobal(ptr);
                            fListViewHasFocus = ListViewController.Handle == PInvoke.GetFocus();
                            msg.Result = (IntPtr)0x20;
                            return true;
                        }
                }
            }
            return false;
        }

        private void OnFileRename(IDLWrapper idl) {
            if(!idl.Available || idl.IsFolder) return;
            string path = idl.Path;
            if(File.Exists(path)) {
                string extension = Path.GetExtension(path);
                if(!string.IsNullOrEmpty(extension) && (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || extension.Equals(".url", StringComparison.OrdinalIgnoreCase))) {
                    return;
                }
            }
            IntPtr hWnd = GetEditControl();
            if(hWnd == IntPtr.Zero) return;

            IntPtr lParam = Marshal.AllocHGlobal(520);
            if((int)PInvoke.SendMessage(hWnd, WM.GETTEXT, (IntPtr)260, lParam) > 0) {
                string str3 = Marshal.PtrToStringUni(lParam);
                if(str3.Length > 2) {
                    int num = str3.LastIndexOf(".");
                    if(num > 0) {
                        // Explorer will send the EM_SETSEL message to select the
                        // entire filename.  We will intercept this message and
                        // change the params to select only the part before the
                        // extension.
                        EditController = new NativeWindowController(hWnd);
                        EditController.OptionalHandle = (IntPtr)num;
                        EditController.MessageCaptured += EditController_MessageCaptured;
                    }
                }
            }
            Marshal.FreeHGlobal(lParam);
        }

        private RECT GetLVITEMRECT(IntPtr hwnd, int iItem, bool fSubDirTip, int fvm) {
            int code;
            bool flag = false;
            bool flag2 = false;
            if(fSubDirTip) {
                switch(fvm) {
                    case FVM.ICON:
                        flag = !QTUtility.IsVista;
                        code = LVIR.ICON;
                        break;

                    case FVM.DETAILS:
                        code = LVIR.LABEL;
                        break;

                    case FVM.LIST:
                        if(QTUtility.IsVista) {
                            code = LVIR.LABEL;
                        }
                        else {
                            flag2 = true;
                            code = LVIR.ICON;
                        }
                        break;

                    case FVM.TILE:
                        code = LVIR.ICON;
                        break;

                    default:
                        code = LVIR.BOUNDS;
                        break;
                }
            }
            else {
                code = (fvm == FVM.DETAILS) ? LVIR.LABEL : LVIR.BOUNDS;
            }

            RECT rect = new RECT();
            rect.left = code;
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(rect));
            Marshal.StructureToPtr(rect, ptr, false);
            PInvoke.SendMessage(Handle, LVM.GETITEMRECT, (IntPtr)iItem, ptr);
            rect = (RECT)Marshal.PtrToStructure(ptr, typeof(RECT));
            Marshal.FreeHGlobal(ptr);
            PInvoke.MapWindowPoints(Handle, IntPtr.Zero, ref rect, 2);

            if(flag) {
                if((fvm == FVM.THUMBNAIL) || (fvm == FVM.THUMBSTRIP)) {
                    rect.right -= 13;
                    return rect;
                }
                int num3 = (int)PInvoke.SendMessage(hwnd, LVM.GETITEMSPACING, IntPtr.Zero, IntPtr.Zero);
                Size iconSize = SystemInformation.IconSize;
                rect.right = ((rect.left + (((num3 & 0xffff) - iconSize.Width) / 2)) + iconSize.Width) + 8;
                rect.bottom = (rect.top + iconSize.Height) + 6;
                return rect;
            }
            if(flag2) {
                LVITEM structure = new LVITEM();
                structure.pszText = Marshal.AllocHGlobal(520);
                structure.cchTextMax = 260;
                structure.iItem = iItem;
                structure.mask = 1;
                IntPtr zero = Marshal.AllocHGlobal(Marshal.SizeOf(structure));
                Marshal.StructureToPtr(structure, zero, false);
                PInvoke.SendMessage(hwnd, LVM.GETITEM, IntPtr.Zero, zero);
                int num4 = (int)PInvoke.SendMessage(hwnd, LVM.GETSTRINGWIDTH, IntPtr.Zero, structure.pszText);
                num4 += 20;
                Marshal.FreeHGlobal(structure.pszText);
                Marshal.FreeHGlobal(zero);
                rect.right += num4;
                rect.top += 2;
                rect.bottom += 2;
            }
            return rect;
        }

        public override int HitTest(Point pt, bool screenCoords) {
            if(screenCoords) {
                PInvoke.ScreenToClient(ListViewController.Handle, ref pt);
            }
            LVHITTESTINFO structure = new LVHITTESTINFO();
            structure.pt.x = pt.X;
            structure.pt.y = pt.Y;
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(structure));
            Marshal.StructureToPtr(structure, ptr, false);
            int num = (int)PInvoke.SendMessage(ListViewController.Handle, LVM.HITTEST, IntPtr.Zero, ptr);
            Marshal.FreeHGlobal(ptr);
            return num;
        }

        public override bool HotItemIsSelected() {
            // TODO: I don't think HOTITEM means what you think it does.
            int hot = (int)PInvoke.SendMessage(ListViewController.Handle, LVM.GETHOTITEM, IntPtr.Zero, IntPtr.Zero);
            if(hot == -1) return false;
            int state = (int)PInvoke.SendMessage(ListViewController.Handle, LVM.GETITEMSTATE, (IntPtr)hot, (IntPtr)LVIS.SELECTED);
            return ((state & LVIS.SELECTED) != 0);
        }

        public override bool IsTrackingItemName() {
            if(ViewMode == FVM.DETAILS) return true;
            if(GetItemCount() == 0) return false;
            RECT rect = PInvoke.ListView_GetItemRect(ListViewController.Handle, 0, 0, 2);
            Point mousePosition = Control.MousePosition;
            PInvoke.MapWindowPoints(IntPtr.Zero, ListViewController.Handle, ref mousePosition, 1);
            return (Math.Min(rect.left, rect.right) <= mousePosition.X) && (mousePosition.X <= Math.Max(rect.left, rect.right));
        }

        protected override bool ListViewController_MessageCaptured(ref Message msg) {
            if(base.ListViewController_MessageCaptured(ref msg)) {
                return true;
            }

            switch(msg.Msg) {
                // Style flags are reset when the view is changed.
                case LVM.SETVIEW:
                    SetStyleFlags();
                    break;

                // On Vista/7, we don't get a LVM.SETVIEW, but we do
                // get this.
                case WM.SETREDRAW:
                    if(msg.WParam != IntPtr.Zero) {
                        SetStyleFlags();
                    }
                    break;

            }
            return false;
        }

        public override bool PointIsBackground(Point pt, bool screenCoords) {
            if(screenCoords) {
                PInvoke.ScreenToClient(ListViewController.Handle, ref pt);
            }
            LVHITTESTINFO structure = new LVHITTESTINFO();
            structure.pt.x = pt.X;
            structure.pt.y = pt.Y;
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(structure));
            Marshal.StructureToPtr(structure, ptr, false);
            if(QTUtility.IsVista) {
                PInvoke.SendMessage(ListViewController.Handle, LVM.HITTEST, (IntPtr)(-1), ptr);
                structure = (LVHITTESTINFO)Marshal.PtrToStructure(ptr, typeof(LVHITTESTINFO));
                Marshal.FreeHGlobal(ptr);
                return structure.flags == 1 /* LVHT_NOWHERE */;
            }
            else {
                int num = (int)PInvoke.SendMessage(ListViewController.Handle, LVM.HITTEST, IntPtr.Zero, ptr);
                Marshal.FreeHGlobal(ptr);
                return num == -1;
            }
        }
    }
}