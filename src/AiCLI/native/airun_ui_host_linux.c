#include "airun_ui_host.h"
#include <string.h>

#ifdef __linux__

#include <X11/Xlib.h>
#include <X11/Xutil.h>
#include <ctype.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <unistd.h>

typedef struct {
    int64_t handle;
    Window window;
    GC gc;
    int close_pending;
    int width;
    int height;
} NativeUiLinuxWindowSlot;

static Display* g_native_ui_display = NULL;
static int g_native_ui_screen = 0;
static Colormap g_native_ui_colormap = 0;
static int64_t g_native_ui_next_handle = 1;
static NativeUiLinuxWindowSlot g_native_ui_windows[8];

static int native_ui_linux_init(void)
{
    if (g_native_ui_display != NULL) {
        return 1;
    }
    g_native_ui_display = XOpenDisplay(NULL);
    if (g_native_ui_display == NULL) {
        return 0;
    }
    g_native_ui_screen = DefaultScreen(g_native_ui_display);
    g_native_ui_colormap = DefaultColormap(g_native_ui_display, g_native_ui_screen);
    return 1;
}

static NativeUiLinuxWindowSlot* native_ui_linux_find_slot(int64_t handle)
{
    size_t i;
    if (handle <= 0) {
        return NULL;
    }
    for (i = 0U; i < sizeof(g_native_ui_windows) / sizeof(g_native_ui_windows[0]); i += 1U) {
        if (g_native_ui_windows[i].handle == handle && g_native_ui_windows[i].window != 0) {
            return &g_native_ui_windows[i];
        }
    }
    return NULL;
}

static NativeUiLinuxWindowSlot* native_ui_linux_find_slot_by_window(Window window)
{
    size_t i;
    for (i = 0U; i < sizeof(g_native_ui_windows) / sizeof(g_native_ui_windows[0]); i += 1U) {
        if (g_native_ui_windows[i].window == window) {
            return &g_native_ui_windows[i];
        }
    }
    return NULL;
}

static NativeUiLinuxWindowSlot* native_ui_linux_find_empty_slot(void)
{
    size_t i;
    for (i = 0U; i < sizeof(g_native_ui_windows) / sizeof(g_native_ui_windows[0]); i += 1U) {
        if (g_native_ui_windows[i].window == 0) {
            return &g_native_ui_windows[i];
        }
    }
    return NULL;
}

static unsigned long native_ui_linux_parse_color(const char* color, unsigned long fallback_pixel)
{
    XColor xcolor;
    if (g_native_ui_display == NULL || color == NULL || color[0] == '\0') {
        return fallback_pixel;
    }
    if (XParseColor(g_native_ui_display, g_native_ui_colormap, color, &xcolor) == 0) {
        return fallback_pixel;
    }
    if (XAllocColor(g_native_ui_display, g_native_ui_colormap, &xcolor) == 0) {
        return fallback_pixel;
    }
    return xcolor.pixel;
}

static const char* native_ui_linux_path_skip(const char* cursor)
{
    while (cursor != NULL && *cursor != '\0') {
        if (!isspace((unsigned char)*cursor) && *cursor != ',') {
            break;
        }
        cursor += 1;
    }
    return cursor;
}

static int native_ui_linux_path_parse_number(const char** io_cursor, double* out_value)
{
    char* end_ptr = NULL;
    const char* cursor;
    if (io_cursor == NULL || *io_cursor == NULL || out_value == NULL) {
        return 0;
    }
    cursor = native_ui_linux_path_skip(*io_cursor);
    if (cursor == NULL || *cursor == '\0') {
        return 0;
    }
    *out_value = strtod(cursor, &end_ptr);
    if (end_ptr == cursor) {
        return 0;
    }
    *io_cursor = end_ptr;
    return 1;
}

void native_host_ui_reset(void)
{
    size_t i;
    for (i = 0U; i < sizeof(g_native_ui_windows) / sizeof(g_native_ui_windows[0]); i += 1U) {
        g_native_ui_windows[i].handle = 0;
        g_native_ui_windows[i].window = 0;
        g_native_ui_windows[i].gc = 0;
        g_native_ui_windows[i].close_pending = 0;
        g_native_ui_windows[i].width = 0;
        g_native_ui_windows[i].height = 0;
    }
    g_native_ui_next_handle = 1;
}

void native_host_ui_shutdown(void)
{
    size_t i;
    if (g_native_ui_display == NULL) {
        return;
    }
    for (i = 0U; i < sizeof(g_native_ui_windows) / sizeof(g_native_ui_windows[0]); i += 1U) {
        if (g_native_ui_windows[i].window != 0) {
            if (g_native_ui_windows[i].gc != 0) {
                XFreeGC(g_native_ui_display, g_native_ui_windows[i].gc);
            }
            XDestroyWindow(g_native_ui_display, g_native_ui_windows[i].window);
            g_native_ui_windows[i].window = 0;
            g_native_ui_windows[i].gc = 0;
        }
    }
    XFlush(g_native_ui_display);
    XCloseDisplay(g_native_ui_display);
    g_native_ui_display = NULL;
}

int native_host_ui_create_window(const char* title, int width, int height, int64_t* out_handle)
{
    NativeUiLinuxWindowSlot* slot;
    Window window;
    XGCValues gc_values;
    unsigned long event_mask = ExposureMask | KeyPressMask | ButtonPressMask | StructureNotifyMask;
    XSizeHints hints;
    if (out_handle == NULL || width <= 0 || height <= 0) {
        return 0;
    }
    *out_handle = 0;
    if (!native_ui_linux_init()) {
        return 0;
    }
    slot = native_ui_linux_find_empty_slot();
    if (slot == NULL) {
        return 0;
    }
    window = XCreateSimpleWindow(
        g_native_ui_display,
        RootWindow(g_native_ui_display, g_native_ui_screen),
        120,
        120,
        (unsigned int)width,
        (unsigned int)height,
        1,
        BlackPixel(g_native_ui_display, g_native_ui_screen),
        WhitePixel(g_native_ui_display, g_native_ui_screen));
    if (window == 0) {
        return 0;
    }
    XSelectInput(g_native_ui_display, window, event_mask);
    if (title != NULL && title[0] != '\0') {
        XStoreName(g_native_ui_display, window, title);
    } else {
        XStoreName(g_native_ui_display, window, "AiLang");
    }
    memset(&hints, 0, sizeof(hints));
    hints.flags = PSize | PMinSize;
    hints.width = width;
    hints.height = height;
    hints.min_width = 120;
    hints.min_height = 80;
    XSetWMNormalHints(g_native_ui_display, window, &hints);
    XMapWindow(g_native_ui_display, window);
    memset(&gc_values, 0, sizeof(gc_values));
    slot->gc = XCreateGC(g_native_ui_display, window, 0, &gc_values);
    if (slot->gc == 0) {
        XDestroyWindow(g_native_ui_display, window);
        return 0;
    }
    slot->handle = g_native_ui_next_handle++;
    slot->window = window;
    slot->close_pending = 0;
    slot->width = width;
    slot->height = height;
    *out_handle = slot->handle;
    XFlush(g_native_ui_display);
    return 1;
}

int native_host_ui_close_window(int64_t handle)
{
    NativeUiLinuxWindowSlot* slot = native_ui_linux_find_slot(handle);
    if (slot == NULL || g_native_ui_display == NULL) {
        return 0;
    }
    if (slot->gc != 0) {
        XFreeGC(g_native_ui_display, slot->gc);
    }
    XDestroyWindow(g_native_ui_display, slot->window);
    slot->handle = 0;
    slot->window = 0;
    slot->gc = 0;
    slot->close_pending = 1;
    slot->width = 0;
    slot->height = 0;
    XFlush(g_native_ui_display);
    return 1;
}

int native_host_ui_begin_frame(int64_t handle)
{
    NativeUiLinuxWindowSlot* slot = native_ui_linux_find_slot(handle);
    unsigned long bg;
    if (slot == NULL || g_native_ui_display == NULL) {
        return 0;
    }
    bg = WhitePixel(g_native_ui_display, g_native_ui_screen);
    XSetForeground(g_native_ui_display, slot->gc, bg);
    XFillRectangle(g_native_ui_display, slot->window, slot->gc, 0, 0, (unsigned int)slot->width, (unsigned int)slot->height);
    return 1;
}

int native_host_ui_end_frame(int64_t handle)
{
    (void)handle;
    return 1;
}

int native_host_ui_present(int64_t handle)
{
    NativeUiLinuxWindowSlot* slot = native_ui_linux_find_slot(handle);
    if (slot == NULL || g_native_ui_display == NULL) {
        return 0;
    }
    XFlush(g_native_ui_display);
    return 1;
}

int native_host_ui_wait_frame(int64_t handle)
{
    (void)handle;
    usleep(16000U);
    return 1;
}

int native_host_ui_draw_rect(int64_t handle, int x, int y, int width, int height, const char* color)
{
    NativeUiLinuxWindowSlot* slot = native_ui_linux_find_slot(handle);
    unsigned long pixel;
    if (slot == NULL || g_native_ui_display == NULL || width <= 0 || height <= 0) {
        return 0;
    }
    pixel = native_ui_linux_parse_color(color, BlackPixel(g_native_ui_display, g_native_ui_screen));
    XSetForeground(g_native_ui_display, slot->gc, pixel);
    XFillRectangle(g_native_ui_display, slot->window, slot->gc, x, y, (unsigned int)width, (unsigned int)height);
    return 1;
}

int native_host_ui_draw_ellipse(int64_t handle, int x, int y, int width, int height, const char* color)
{
    NativeUiLinuxWindowSlot* slot = native_ui_linux_find_slot(handle);
    unsigned long pixel;
    if (slot == NULL || g_native_ui_display == NULL || width <= 0 || height <= 0) {
        return 0;
    }
    pixel = native_ui_linux_parse_color(color, BlackPixel(g_native_ui_display, g_native_ui_screen));
    XSetForeground(g_native_ui_display, slot->gc, pixel);
    XFillArc(g_native_ui_display, slot->window, slot->gc, x, y, (unsigned int)width, (unsigned int)height, 0, 360 * 64);
    return 1;
}

int native_host_ui_draw_text(int64_t handle, int x, int y, const char* text, const char* color, int font_size)
{
    NativeUiLinuxWindowSlot* slot = native_ui_linux_find_slot(handle);
    unsigned long pixel;
    (void)font_size;
    if (slot == NULL || g_native_ui_display == NULL || text == NULL) {
        return 0;
    }
    pixel = native_ui_linux_parse_color(color, BlackPixel(g_native_ui_display, g_native_ui_screen));
    XSetForeground(g_native_ui_display, slot->gc, pixel);
    XDrawString(g_native_ui_display, slot->window, slot->gc, x, y, text, (int)strlen(text));
    return 1;
}

int native_host_ui_draw_line(int64_t handle, int x1, int y1, int x2, int y2, const char* color, int stroke_width)
{
    NativeUiLinuxWindowSlot* slot = native_ui_linux_find_slot(handle);
    unsigned long pixel;
    if (slot == NULL || g_native_ui_display == NULL) {
        return 0;
    }
    pixel = native_ui_linux_parse_color(color, BlackPixel(g_native_ui_display, g_native_ui_screen));
    XSetForeground(g_native_ui_display, slot->gc, pixel);
    if (stroke_width > 0) {
        XSetLineAttributes(g_native_ui_display, slot->gc, (unsigned int)stroke_width, LineSolid, CapButt, JoinMiter);
    }
    XDrawLine(g_native_ui_display, slot->window, slot->gc, x1, y1, x2, y2);
    return 1;
}

int native_host_ui_draw_path(int64_t handle, const char* path, const char* color, int stroke_width)
{
    NativeUiLinuxWindowSlot* slot = native_ui_linux_find_slot(handle);
    unsigned long pixel;
    const char* cursor;
    char cmd = '\0';
    double x = 0.0;
    double y = 0.0;
    double start_x = 0.0;
    double start_y = 0.0;
    int has_point = 0;
    if (slot == NULL || g_native_ui_display == NULL || path == NULL) {
        return 0;
    }
    pixel = native_ui_linux_parse_color(color, BlackPixel(g_native_ui_display, g_native_ui_screen));
    XSetForeground(g_native_ui_display, slot->gc, pixel);
    if (stroke_width > 0) {
        XSetLineAttributes(g_native_ui_display, slot->gc, (unsigned int)stroke_width, LineSolid, CapButt, JoinMiter);
    }
    cursor = path;
    while (cursor != NULL && *cursor != '\0') {
        double a = 0.0;
        double b = 0.0;
        cursor = native_ui_linux_path_skip(cursor);
        if (*cursor == '\0') {
            break;
        }
        if (isalpha((unsigned char)*cursor)) {
            cmd = *cursor;
            cursor += 1;
            if (cmd == 'Z' || cmd == 'z') {
                if (has_point) {
                    XDrawLine(g_native_ui_display, slot->window, slot->gc, (int)x, (int)y, (int)start_x, (int)start_y);
                    x = start_x;
                    y = start_y;
                }
                cmd = '\0';
            }
            continue;
        }
        if (cmd == '\0') {
            return 0;
        }
        if (cmd == 'M' || cmd == 'm' || cmd == 'L' || cmd == 'l') {
            if (!native_ui_linux_path_parse_number(&cursor, &a) || !native_ui_linux_path_parse_number(&cursor, &b)) {
                return 0;
            }
            if (cmd == 'm' || cmd == 'l') {
                a += x;
                b += y;
            }
            if (cmd == 'M' || cmd == 'm') {
                x = a;
                y = b;
                start_x = x;
                start_y = y;
                has_point = 1;
                cmd = (cmd == 'm') ? 'l' : 'L';
            } else {
                XDrawLine(g_native_ui_display, slot->window, slot->gc, (int)x, (int)y, (int)a, (int)b);
                x = a;
                y = b;
                has_point = 1;
            }
            continue;
        }
        if (cmd == 'H' || cmd == 'h') {
            if (!native_ui_linux_path_parse_number(&cursor, &a)) {
                return 0;
            }
            a = (cmd == 'h') ? (x + a) : a;
            XDrawLine(g_native_ui_display, slot->window, slot->gc, (int)x, (int)y, (int)a, (int)y);
            x = a;
            has_point = 1;
            continue;
        }
        if (cmd == 'V' || cmd == 'v') {
            if (!native_ui_linux_path_parse_number(&cursor, &a)) {
                return 0;
            }
            a = (cmd == 'v') ? (y + a) : a;
            XDrawLine(g_native_ui_display, slot->window, slot->gc, (int)x, (int)y, (int)x, (int)a);
            y = a;
            has_point = 1;
            continue;
        }
        return 0;
    }
    return 1;
}

int native_host_ui_poll_event(int64_t handle, NativeHostUiEvent* out_event)
{
    NativeUiLinuxWindowSlot* wanted_slot = native_ui_linux_find_slot(handle);
    XEvent event;
    if (out_event == NULL || g_native_ui_display == NULL || wanted_slot == NULL) {
        return 0;
    }
    memset(out_event, 0, sizeof(*out_event));
    (void)snprintf(out_event->type, sizeof(out_event->type), "none");

    while (XPending(g_native_ui_display) > 0) {
        NativeUiLinuxWindowSlot* slot = NULL;
        XNextEvent(g_native_ui_display, &event);
        slot = native_ui_linux_find_slot_by_window(event.xany.window);
        if (slot == NULL || slot->handle != handle) {
            continue;
        }
        if (event.type == DestroyNotify) {
            slot->close_pending = 1;
            (void)snprintf(out_event->type, sizeof(out_event->type), "closed");
            return 1;
        }
        if (event.type == ConfigureNotify) {
            slot->width = event.xconfigure.width;
            slot->height = event.xconfigure.height;
            continue;
        }
        if (event.type == KeyPress) {
            KeySym keysym = NoSymbol;
            char keybuf[32];
            int len = XLookupString(&event.xkey, keybuf, (int)sizeof(keybuf) - 1, &keysym, NULL);
            (void)keysym;
            keybuf[(len > 0) ? len : 0] = '\0';
            (void)snprintf(out_event->type, sizeof(out_event->type), "key");
            (void)snprintf(out_event->key, sizeof(out_event->key), "%s", keybuf);
            (void)snprintf(out_event->text, sizeof(out_event->text), "%s", keybuf);
            out_event->modifiers = 0;
            out_event->repeat = 0;
            return 1;
        }
        if (event.type == ButtonPress) {
            (void)snprintf(out_event->type, sizeof(out_event->type), "click");
            out_event->x = event.xbutton.x;
            out_event->y = event.xbutton.y;
            return 1;
        }
    }
    if (wanted_slot->close_pending != 0) {
        (void)snprintf(out_event->type, sizeof(out_event->type), "closed");
        return 1;
    }
    return 1;
}

int native_host_ui_get_window_size(int64_t handle, int* out_width, int* out_height)
{
    NativeUiLinuxWindowSlot* slot = native_ui_linux_find_slot(handle);
    XWindowAttributes attrs;
    if (slot == NULL || g_native_ui_display == NULL) {
        return 0;
    }
    if (XGetWindowAttributes(g_native_ui_display, slot->window, &attrs) != 0) {
        slot->width = attrs.width;
        slot->height = attrs.height;
    }
    if (out_width != NULL) {
        *out_width = slot->width;
    }
    if (out_height != NULL) {
        *out_height = slot->height;
    }
    return 1;
}

#else
#error "airun_ui_host_linux.c must only be compiled for linux targets. Use airun_ui_host_unavailable.c on unsupported targets."
#endif
