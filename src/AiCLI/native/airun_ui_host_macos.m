#include "airun_ui_host.h"

#ifdef __APPLE__

#import <AppKit/AppKit.h>
#include <math.h>
#include <stdio.h>
#include <string.h>

typedef struct {
    int64_t handle;
    NSWindow* window;
    id delegate_ref;
    int close_pending;
} NativeUiWindowSlot;

static NativeUiWindowSlot g_native_ui_windows[8];
static int64_t g_native_ui_next_handle = 1;
static int g_native_ui_app_initialized = 0;

static NativeUiWindowSlot* native_ui_find_slot(int64_t handle)
{
    size_t i;
    if (handle <= 0) {
        return NULL;
    }
    for (i = 0U; i < sizeof(g_native_ui_windows) / sizeof(g_native_ui_windows[0]); i += 1U) {
        if (g_native_ui_windows[i].handle == handle && g_native_ui_windows[i].window != nil) {
            return &g_native_ui_windows[i];
        }
    }
    return NULL;
}

static NativeUiWindowSlot* native_ui_find_empty_slot(void)
{
    size_t i;
    for (i = 0U; i < sizeof(g_native_ui_windows) / sizeof(g_native_ui_windows[0]); i += 1U) {
        if (g_native_ui_windows[i].window == nil) {
            return &g_native_ui_windows[i];
        }
    }
    return NULL;
}

static int native_ui_init_app(void)
{
    if (g_native_ui_app_initialized != 0) {
        return 1;
    }
    [NSApplication sharedApplication];
    [NSApp setActivationPolicy:NSApplicationActivationPolicyRegular];
    [NSApp finishLaunching];
    g_native_ui_app_initialized = 1;
    return 1;
}

@interface NativeUiWindowDelegate : NSObject<NSWindowDelegate>
@property(nonatomic, assign) NativeUiWindowSlot* slot;
@end

@implementation NativeUiWindowDelegate
- (void)windowWillClose:(NSNotification*)notification
{
    (void)notification;
    if (self.slot != NULL) {
        self.slot->close_pending = 1;
    }
}
@end

static void native_ui_set_string(char* out, size_t out_capacity, const char* text)
{
    if (out == NULL || out_capacity == 0U) {
        return;
    }
    if (text == NULL) {
        out[0] = '\0';
        return;
    }
    (void)snprintf(out, out_capacity, "%s", text);
}

static const char* native_ui_normalize_key(NSEvent* event)
{
    unsigned short key_code;
    NSString* chars;
    if (event == nil) {
        return "";
    }
    key_code = [event keyCode];
    switch (key_code) {
        case 36:
        case 76:
            return "enter";
        case 48:
            return "tab";
        case 49:
            return "space";
        case 51:
            return "backspace";
        case 53:
            return "escape";
        case 123:
            return "left";
        case 124:
            return "right";
        case 125:
            return "down";
        case 126:
            return "up";
        case 117:
            return "delete";
        default:
            break;
    }
    chars = [event charactersIgnoringModifiers];
    if (chars == nil || [chars length] == 0U) {
        return "";
    }
    return [[chars lowercaseString] UTF8String];
}

static void native_ui_set_text_from_event(char* out, size_t out_capacity, NSEvent* event)
{
    NSString* chars;
    if (out == NULL || out_capacity == 0U) {
        return;
    }
    out[0] = '\0';
    if (event == nil) {
        return;
    }
    chars = [event characters];
    if (chars == nil || [chars length] == 0U) {
        return;
    }
    native_ui_set_string(out, out_capacity, [chars UTF8String]);
}

void native_host_ui_reset(void)
{
    size_t i;
    for (i = 0U; i < sizeof(g_native_ui_windows) / sizeof(g_native_ui_windows[0]); i += 1U) {
        g_native_ui_windows[i].handle = 0;
        g_native_ui_windows[i].window = nil;
        g_native_ui_windows[i].delegate_ref = nil;
        g_native_ui_windows[i].close_pending = 0;
    }
    g_native_ui_next_handle = 1;
}

void native_host_ui_shutdown(void)
{
    size_t i;
    for (i = 0U; i < sizeof(g_native_ui_windows) / sizeof(g_native_ui_windows[0]); i += 1U) {
        if (g_native_ui_windows[i].window != nil) {
            [g_native_ui_windows[i].window close];
            g_native_ui_windows[i].window = nil;
            g_native_ui_windows[i].delegate_ref = nil;
            g_native_ui_windows[i].handle = 0;
            g_native_ui_windows[i].close_pending = 0;
        }
    }
}

int native_host_ui_create_window(const char* title, int width, int height, int64_t* out_handle)
{
    NativeUiWindowSlot* slot;
    NSRect frame;
    NSWindow* window;
    NativeUiWindowDelegate* delegate;
    NSString* window_title;
    if (out_handle == NULL || width <= 0 || height <= 0) {
        return 0;
    }
    *out_handle = 0;
    if (!native_ui_init_app()) {
        return 0;
    }
    slot = native_ui_find_empty_slot();
    if (slot == NULL) {
        return 0;
    }
    frame = NSMakeRect(120, 120, width, height);
    window = [[NSWindow alloc] initWithContentRect:frame
                                         styleMask:(NSWindowStyleMaskTitled |
                                                    NSWindowStyleMaskClosable |
                                                    NSWindowStyleMaskResizable |
                                                    NSWindowStyleMaskMiniaturizable)
                                           backing:NSBackingStoreBuffered
                                             defer:NO];
    if (window == nil) {
        return 0;
    }
    window_title = [NSString stringWithUTF8String:(title == NULL || title[0] == '\0') ? "AiLang" : title];
    [window setTitle:window_title];
    [window setReleasedWhenClosed:NO];
    delegate = [NativeUiWindowDelegate new];
    delegate.slot = slot;
    [window setDelegate:delegate];
    [window makeKeyAndOrderFront:nil];
    [NSApp activateIgnoringOtherApps:YES];
    slot->handle = g_native_ui_next_handle++;
    slot->window = window;
    slot->delegate_ref = delegate;
    slot->close_pending = 0;
    *out_handle = slot->handle;
    return 1;
}

int native_host_ui_close_window(int64_t handle)
{
    NativeUiWindowSlot* slot = native_ui_find_slot(handle);
    if (slot == NULL) {
        return 0;
    }
    [slot->window close];
    slot->close_pending = 1;
    return 1;
}

int native_host_ui_begin_frame(int64_t handle)
{
    return native_ui_find_slot(handle) != NULL ? 1 : 0;
}

int native_host_ui_end_frame(int64_t handle)
{
    return native_ui_find_slot(handle) != NULL ? 1 : 0;
}

int native_host_ui_present(int64_t handle)
{
    NativeUiWindowSlot* slot = native_ui_find_slot(handle);
    if (slot == NULL) {
        return 0;
    }
    [slot->window displayIfNeeded];
    return 1;
}

int native_host_ui_wait_frame(int64_t handle)
{
    if (native_ui_find_slot(handle) == NULL) {
        return 0;
    }
    [NSThread sleepForTimeInterval:(1.0 / 60.0)];
    return 1;
}

int native_host_ui_draw_rect(int64_t handle, int x, int y, int width, int height, const char* color)
{
    (void)x;
    (void)y;
    (void)width;
    (void)height;
    (void)color;
    return native_ui_find_slot(handle) != NULL ? 1 : 0;
}

int native_host_ui_draw_text(int64_t handle, int x, int y, const char* text, const char* color, int font_size)
{
    (void)x;
    (void)y;
    (void)text;
    (void)color;
    (void)font_size;
    return native_ui_find_slot(handle) != NULL ? 1 : 0;
}

int native_host_ui_draw_line(int64_t handle, int x1, int y1, int x2, int y2, const char* color, int stroke_width)
{
    (void)x1;
    (void)y1;
    (void)x2;
    (void)y2;
    (void)color;
    (void)stroke_width;
    return native_ui_find_slot(handle) != NULL ? 1 : 0;
}

int native_host_ui_draw_path(int64_t handle, const char* path, const char* color, int stroke_width)
{
    (void)path;
    (void)color;
    (void)stroke_width;
    return native_ui_find_slot(handle) != NULL ? 1 : 0;
}

int native_host_ui_poll_event(int64_t handle, NativeHostUiEvent* out_event)
{
    NativeUiWindowSlot* slot = native_ui_find_slot(handle);
    NSEvent* event;
    if (slot == NULL || out_event == NULL) {
        return 0;
    }
    memset(out_event, 0, sizeof(*out_event));
    native_ui_set_string(out_event->type, sizeof(out_event->type), "none");
    if (slot->close_pending != 0) {
        native_ui_set_string(out_event->type, sizeof(out_event->type), "closed");
        slot->close_pending = 0;
        return 1;
    }
    event = [NSApp nextEventMatchingMask:NSEventMaskAny
                               untilDate:[NSDate dateWithTimeIntervalSinceNow:0.0]
                                  inMode:NSDefaultRunLoopMode
                                 dequeue:YES];
    if (event == nil) {
        return 1;
    }
    switch ([event type]) {
        case NSEventTypeLeftMouseDown: {
            NSWindow* source_window = [event window];
            NSPoint location = [event locationInWindow];
            NSRect content_bounds;
            int y_top;
            if (source_window == nil) {
                source_window = slot->window;
            }
            content_bounds = [[source_window contentView] bounds];
            y_top = (int)llround(content_bounds.size.height - location.y);
            out_event->x = (int)llround(location.x);
            out_event->y = y_top < 0 ? 0 : y_top;
            native_ui_set_string(out_event->type, sizeof(out_event->type), "click");
            break;
        }
        case NSEventTypeKeyDown: {
            NSUInteger flags = [event modifierFlags];
            const char* key_name = native_ui_normalize_key(event);
            native_ui_set_string(out_event->type, sizeof(out_event->type), "key");
            native_ui_set_string(out_event->key, sizeof(out_event->key), key_name);
            native_ui_set_text_from_event(out_event->text, sizeof(out_event->text), event);
            out_event->repeat = [event isARepeat] ? 1 : 0;
            out_event->modifiers =
                ((flags & NSEventModifierFlagShift) != 0 ? 1 : 0) |
                ((flags & NSEventModifierFlagControl) != 0 ? 2 : 0) |
                ((flags & NSEventModifierFlagOption) != 0 ? 4 : 0) |
                ((flags & NSEventModifierFlagCommand) != 0 ? 8 : 0);
            break;
        }
        default:
            break;
    }
    [NSApp sendEvent:event];
    [NSApp updateWindows];
    return 1;
}

int native_host_ui_get_window_size(int64_t handle, int* out_width, int* out_height)
{
    NativeUiWindowSlot* slot = native_ui_find_slot(handle);
    NSRect content_bounds;
    if (slot == NULL || out_width == NULL || out_height == NULL) {
        return 0;
    }
    content_bounds = [[slot->window contentView] bounds];
    *out_width = (int)llround(content_bounds.size.width);
    *out_height = (int)llround(content_bounds.size.height);
    return 1;
}

#endif
