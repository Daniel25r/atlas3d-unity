// ATLAS 3D - MP4 (H.264) video from the rendered frames, with Android's own
// encoder (MediaCodec + MediaMuxer). The colours are written plane by plane
// as the encoder describes them (Image planes: row and pixel strides), so
// they come out right on every phone (the Godot version guessed the layout
// and some phones showed wrong colours).
package org.atlas3d;

import android.media.Image;
import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaFormat;
import android.media.MediaMuxer;
import java.nio.ByteBuffer;

public class AtlasVideoEncoder {
    private MediaCodec codec;
    private MediaMuxer muxer;
    private final MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();
    private int track = -1;
    private boolean muxing = false;
    private int width, height, fps;
    private long frameIndex = 0;
    public String error = "";

    public boolean start(String path, int w, int h, int framesPerSecond, int bitrate) {
        try {
            width = w;
            height = h;
            fps = Math.max(1, framesPerSecond);
            MediaFormat f = MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, w, h);
            f.setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatYUV420Flexible);
            f.setInteger(MediaFormat.KEY_BIT_RATE, bitrate);
            f.setInteger(MediaFormat.KEY_FRAME_RATE, fps);
            f.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 1);
            codec = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_VIDEO_AVC);
            codec.configure(f, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
            codec.start();
            muxer = new MediaMuxer(path, MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4);
            return true;
        } catch (Exception e) {
            error = e.toString();
            release();
            return false;
        }
    }

    /** rgb: width*height*3 bytes, rows from the bottom (as Unity reads them). */
    public boolean addFrame(byte[] rgb) {
        try {
            int idx = -1;
            for (int tries = 0; tries < 200 && idx < 0; tries++) {
                idx = codec.dequeueInputBuffer(10000);
                if (idx < 0) drain(false);
            }
            if (idx < 0) { error = "el codificador no acepta más imágenes"; return false; }
            Image img = codec.getInputImage(idx);
            if (img == null) { error = "sin imagen de entrada"; return false; }
            fill(img, rgb);
            long pts = frameIndex * 1000000L / fps;
            codec.queueInputBuffer(idx, 0, width * height * 3 / 2, pts, 0);
            frameIndex++;
            drain(false);
            return true;
        } catch (Exception e) {
            error = e.toString();
            return false;
        }
    }

    private void fill(Image img, byte[] rgb) {
        Image.Plane[] p = img.getPlanes();
        ByteBuffer yb = p[0].getBuffer(), ub = p[1].getBuffer(), vb = p[2].getBuffer();
        int yrs = p[0].getRowStride(), yps = p[0].getPixelStride();
        int urs = p[1].getRowStride(), ups = p[1].getPixelStride();
        int vrs = p[2].getRowStride(), vps = p[2].getPixelStride();
        int w = width, h = height;
        for (int y = 0; y < h; y++) {
            int src = (h - 1 - y) * w * 3;
            int dst = y * yrs;
            for (int x = 0; x < w; x++) {
                int r = rgb[src] & 0xff, g = rgb[src + 1] & 0xff, b = rgb[src + 2] & 0xff;
                src += 3;
                int Y = ((66 * r + 129 * g + 25 * b + 128) >> 8) + 16;
                yb.put(dst + x * yps, (byte) Y);
            }
        }
        for (int y = 0; y < h / 2; y++) {
            for (int x = 0; x < w / 2; x++) {
                int src = ((h - 1 - y * 2) * w + x * 2) * 3;
                int r = rgb[src] & 0xff, g = rgb[src + 1] & 0xff, b = rgb[src + 2] & 0xff;
                int U = ((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128;
                int V = ((112 * r - 94 * g - 18 * b + 128) >> 8) + 128;
                ub.put(y * urs + x * ups, (byte) U);
                vb.put(y * vrs + x * vps, (byte) V);
            }
        }
    }

    private void drain(boolean end) {
        int idle = 0;
        while (true) {
            int o = codec.dequeueOutputBuffer(info, end ? 10000 : 0);
            if (o == MediaCodec.INFO_TRY_AGAIN_LATER) {
                if (!end || ++idle > 300) break;
            } else if (o == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                if (!muxing) {
                    track = muxer.addTrack(codec.getOutputFormat());
                    muxer.start();
                    muxing = true;
                }
            } else if (o >= 0) {
                ByteBuffer out = codec.getOutputBuffer(o);
                if ((info.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) != 0) info.size = 0;
                if (info.size > 0 && muxing && out != null) {
                    out.position(info.offset);
                    out.limit(info.offset + info.size);
                    muxer.writeSampleData(track, out, info);
                }
                codec.releaseOutputBuffer(o, false);
                if ((info.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0) break;
            }
        }
    }

    public boolean finish() {
        try {
            int idx = codec.dequeueInputBuffer(100000);
            if (idx >= 0) codec.queueInputBuffer(idx, 0, 0, frameIndex * 1000000L / fps, MediaCodec.BUFFER_FLAG_END_OF_STREAM);
            drain(true);
            boolean ok = muxing;
            release();
            if (!ok) error = "el vídeo quedó vacío";
            return ok;
        } catch (Exception e) {
            error = e.toString();
            release();
            return false;
        }
    }

    private void release() {
        try { if (codec != null) { codec.stop(); codec.release(); } } catch (Exception e) { }
        try { if (muxer != null) { if (muxing) muxer.stop(); muxer.release(); } } catch (Exception e) { }
        codec = null;
        muxer = null;
        muxing = false;
    }

    public String getError() {
        return error;
    }
}
