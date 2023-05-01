"use strict";
var __extends = (this && this.__extends) || (function () {
    var extendStatics = function (d, b) {
        extendStatics = Object.setPrototypeOf ||
            ({ __proto__: [] } instanceof Array && function (d, b) { d.__proto__ = b; }) ||
            function (d, b) { for (var p in b) if (Object.prototype.hasOwnProperty.call(b, p)) d[p] = b[p]; };
        return extendStatics(d, b);
    };
    return function (d, b) {
        if (typeof b !== "function" && b !== null)
            throw new TypeError("Class extends value " + String(b) + " is not a constructor or null");
        extendStatics(d, b);
        function __() { this.constructor = d; }
        d.prototype = b === null ? Object.create(b) : (__.prototype = b.prototype, new __());
    };
})();
var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    function adopt(value) { return value instanceof P ? value : new P(function (resolve) { resolve(value); }); }
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : adopt(result.value).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
var __generator = (this && this.__generator) || function (thisArg, body) {
    var _ = { label: 0, sent: function() { if (t[0] & 1) throw t[1]; return t[1]; }, trys: [], ops: [] }, f, y, t, g;
    return g = { next: verb(0), "throw": verb(1), "return": verb(2) }, typeof Symbol === "function" && (g[Symbol.iterator] = function() { return this; }), g;
    function verb(n) { return function (v) { return step([n, v]); }; }
    function step(op) {
        if (f) throw new TypeError("Generator is already executing.");
        while (g && (g = 0, op[0] && (_ = 0)), _) try {
            if (f = 1, y && (t = op[0] & 2 ? y["return"] : op[0] ? y["throw"] || ((t = y["return"]) && t.call(y), 0) : y.next) && !(t = t.call(y, op[1])).done) return t;
            if (y = 0, t) op = [op[0] & 2, t.value];
            switch (op[0]) {
                case 0: case 1: t = op; break;
                case 4: _.label++; return { value: op[1], done: false };
                case 5: _.label++; y = op[1]; op = [0]; continue;
                case 7: op = _.ops.pop(); _.trys.pop(); continue;
                default:
                    if (!(t = _.trys, t = t.length > 0 && t[t.length - 1]) && (op[0] === 6 || op[0] === 2)) { _ = 0; continue; }
                    if (op[0] === 3 && (!t || (op[1] > t[0] && op[1] < t[3]))) { _.label = op[1]; break; }
                    if (op[0] === 6 && _.label < t[1]) { _.label = t[1]; t = op; break; }
                    if (t && _.label < t[2]) { _.label = t[2]; _.ops.push(op); break; }
                    if (t[2]) _.ops.pop();
                    _.trys.pop(); continue;
            }
            op = body.call(thisArg, _);
        } catch (e) { op = [6, e]; y = 0; } finally { f = t = 0; }
        if (op[0] & 5) throw op[1]; return { value: op[0] ? op[1] : void 0, done: true };
    }
};
Object.defineProperty(exports, "__esModule", { value: true });
var PDFJS = require("pdfjs-dist/webpack");
var d3 = require("d3");
var $ = require("jquery");
require("bootstrap-multiselect");
var events_1 = require("events");
//https://ralzohairi.medium.com/audio-recording-in-javascript-96eed45b75ee
//https://orangeable.com/javascript/equalizer-web-audio-api
//https://github.com/orangeable/javascript-equalizer/blob/master/js/main.js
//https://orangeable.com/javascript/equalizer-web-audio-api
//https://github.com/orangeable/javascript-equalizer
//https://openai.com/api/
//https://platform.openai.com/examples
//https://blog.teamtreehouse.com/getting-started-speech-synthesis-api#:~:text=To%20use%20a%20voice%2C%20set,speechSynthesis.
//https://developer.mozilla.org/en-US/docs/Web/API/SpeechSynthesis
//https://ourcodeworld.com/articles/read/405/how-to-convert-pdf-to-text-extract-text-from-pdf-with-javascript
//https://medium.com/@david.richards.tech/ai-audio-conversations-with-openai-whisper-3c730a9c7123
var moment = require("moment");
var error_manager_1 = require("./core/error-manager");
var app = /** @class */ (function () {
    function app(userFirstName, profilePicture) {
        var _this = this;
        this.userFirstName = userFirstName;
        this.profilePicture = profilePicture;
        this.errMngr = new error_manager_1.default();
        var welcomeMsg = "<div class=\"modal fade show\" id=\"exampleModal\" tabindex=\"-1\" aria-labelledby=\"exampleModalLabel\" aria-modal=\"true\" role=\"dialog\" style=\"display: block;\" data-mdb-backdrop=\"static\" data-mdb-keyboard=\"false\">\n                             <div class=\"modal-dialog modal-dialog-centered\">\n                                <div class=\"modal-content\">\n                                  <div class=\"modal-header border-0\">\n                                    <h3 class=\"display-6 pt-3 ps-3\">Welcome ".concat(userFirstName, "</h3>\n                                    <img src=\"/img/Lucy.png\" class=\"rounded-circle shadow-1-strong\" width=\"85\" height=\"85\" alt=\"\" aria-controls=\"#picker-editor\" >\n                                  </div>\n                                  <div class=\"modal-body border-0\">\n                                      <div class=\"d-flex justify-content-end align-items-end\">\n                                       <img src=\"/img/edge-logo.webp\" class=\"rounded me-1\" width=\"100\" height=\"100\" alt=\"\" aria-controls=\"#picker-editor\">\n                                       <p class=\"lead\">For better experience, we recommend you to use Microsoft Edge.</p>\n                                      </div>\n                                  </div>\n                                  <div class=\"modal-footer border-0\">\n                                    <button type=\"button\" class=\"btn btn-secondary\" data-mdb-dismiss=\"modal\">OK</button>\n                                  </div>\n                                </div>\n                              </div>\n                              </div>");
        var myModalEl = $(welcomeMsg);
        myModalEl.on('hidden.bs.modal', function (event) {
            // do something...
            _this.initUi();
        });
        myModalEl.modal('toggle');
        $('input[type="file"]').on('change', function (event) {
            event.preventDefault();
            var file = event.target.files[0];
            $('#span-file-info').text('Type: ' + file.type + ', Size: ' + (file.size / 1024) + ' KB');
            var filepath = URL.createObjectURL(file);
            // Todo: complete the MIME list
            //https://developer.mozilla.org/en-US/docs/Web/HTTP/Basics_of_HTTP/MIME_types/Common_types
            switch (file.type) {
                case 'application/pdf':
                    _this.parsePdf(filepath).then(function (textPage) {
                        _this.addToChatBox(textPage);
                    });
                    break;
                case 'audio/mpeg':
                case 'audio/ogg':
                case 'audio/aac':
                case 'audio/opus':
                case 'audio/wav':
                case 'audio/webm':
                case 'audio/3gpp':
                case 'audio/3gpp2':
                case 'audio/x-m4a':
                    _this.transcript(file).then(function (textPage) {
                        _this.addToChatBox(textPage);
                        _this.addToAttachments(file).then(function (data) {
                            $('#ul-chat-attachments').html("<li class=\"list-group-item p-4\">\n                                                <div class=\"d-flex justify-content-between align-items-center\">\n                                                    <div class=\"fw-bold\">".concat(data.filename, "</div>\n                                                    <span class=\"badge rounded-pill badge-success\">").concat(moment.utc(moment.duration(data.audioduration, "seconds").asMilliseconds()).format("HH:mm:ss"), "</span>\n                                                </div>\n\n                                                <div class=\"text-muted\">\n                                                    <audio id=\"audio-data\" preload=\"auto\">\n                                                        <source src=\"").concat(data.audiosrc, "\">\n                                                    </audio>\n                                                    <div id=\"audioplayer d-flex justify-content-between align-items-center\">\n                                                        <i id=\"pButton\" class=\"fas fa-play\"></i>\n                                                        <div id=\"timeline\">\n                                                            <div id=\"playhead\"></div>\n                                                        </div>\n                                                    </div>\n                                                </div>\n                                            </li>"));
                            $('[data-mdb-target="#modal-attachments"]').removeClass('d-none');
                        });
                    });
                    break;
                default:
            }
        });
        $(document).on('click', '#btn-upload-app', function (event) {
            event.preventDefault();
            var files = $('#fil-upload-app');
        });
        $(document).on('click', '#pButton', function (event) {
            event.preventDefault();
            var audio = $('#audio-data').get(0);
            audio.addEventListener('timeupdate', function (event) {
                event.preventDefault();
                var audio = event.currentTarget;
                var position = audio.currentTime / audio.duration;
                var offset = Math.ceil($('#timeline').width() * position);
                $('#playhead').css('transform', "translate(".concat(offset, "px, 0)"));
            });
            audio.addEventListener('ended', function (event) {
                event.preventDefault();
                $('#pButton').toggleClass("fa-play fa-pause");
                $('#playhead').css('transform', "translate(0, 0)");
            });
            if ($(event.currentTarget).get(0).classList.contains('fa-play')) {
                audio.play();
            }
            else {
                audio.pause();
            }
            $('#pButton').toggleClass("fa-play fa-pause");
        });
        //tinymce.init({
        //    selector: "[data-emojiable='true']",
        //    plugins: "emoticons autoresize",
        //    toolbar: "emoticons",
        //    toolbar_location: "bottom",
        //    menubar: false,
        //    statusbar: false
        //});
    }
    app.prototype.initUi = function () {
        var _this = this;
        this.voiceRecognizer = new VoiceRecognizer(this.userFirstName, this.profilePicture);
        this.equalizer = new Equalizer(this.profilePicture);
        $('#button-send-message').on('click', function (event) {
            event.preventDefault();
            var msg = $('#textArea-chat-message').val().toString();
            if (msg && msg.length > 0) {
                _this.addToChatWindow(msg, _this.userFirstName).then(function () {
                    var diagnostic = document.getElementsByClassName('list-unstyled custom-scrollbar').item(0);
                    var lastMsg = document.getElementsByClassName('direct-chat-msg');
                    diagnostic.scrollTo({ top: lastMsg.item(lastMsg.length - 1).offsetTop, behavior: 'smooth' });
                    $('#textArea-chat-message').val('');
                    $('#ul-chat-attachments').html('');
                    $('[data-mdb-target="#modal-attachments"]').addClass('d-none');
                    //tinymce.activeEditor.setContent('');
                    if (msg.toLowerCase().includes('draw') || msg.toLowerCase().includes('paint') || msg.toLowerCase().includes('sketch') || msg.toLowerCase().includes('portray') || msg.toLowerCase().includes('plot')) {
                        _this.voiceRecognizer.draw(msg);
                    }
                    else if (msg.toLowerCase().includes('transcript')) {
                        //this.voiceRecognizer.transcript(msg);
                    }
                    else {
                        var lang = $('#select-languages option').filter(':selected').text();
                        _this.voiceRecognizer.chat(msg, { "lang": lang });
                    }
                });
            }
        });
        $('#mute').on('click', function (event) {
            event.preventDefault();
            $(event.currentTarget).toggleClass('btn-warning btn-primary');
            $(event.currentTarget).find('.fas').toggleClass("fa-microphone-alt fa-microphone-alt-slash");
            if ($(event.currentTarget).find('.fas').hasClass('fa-microphone-alt')) {
                _this.voiceRecognizer.start();
                $('.form-check-label').text('Audio chat is enabled.');
            }
            else {
                _this.voiceRecognizer.stop();
                $('.form-check-label').text('Audio chat is not enabled.');
            }
        });
        $('#flexSwitchCheckChecked').on('click', function (event) {
            //event.preventDefault();
            if ($(event.currentTarget).is(':checked')) {
                _this.voiceRecognizer.start();
                $('.form-check-label').text('Audio chat is enabled.');
            }
            else {
                _this.voiceRecognizer.stop();
                $('.form-check-label').text('Audio chat is not enabled.');
            }
        });
        $('#button-translate-message').on('click', function (event) {
            event.preventDefault();
            var msg = $('#textArea-chat-message').val().toString();
            var lang = $('#select-translation-language option').filter(':selected').text();
            if (msg.length > 0) {
                _this.addToChatWindow("Translate this into ".concat(lang, ": \n                                        <figure class=\"text-center mb-0\">\n                                            <blockquote class=\"blockquote\">\n                                                <p class=\"pb-3\">\n                                                    <i class=\"fas fa-quote-left fa-xs text-primary\"></i>\n                                                    <span class=\"lead font-italic\">").concat(msg, "</span>\n                                                    <i class=\"fas fa-quote-right fa-xs text-primary\"></i>\n                                                </p>\n                                            </blockquote>\n                                        </figure>"), _this.userFirstName).then(function () {
                    var diagnostic = document.getElementsByClassName('list-unstyled custom-scrollbar').item(0);
                    var lastMsg = document.getElementsByClassName('direct-chat-msg');
                    diagnostic.scrollTo({ top: lastMsg.item(lastMsg.length - 1).offsetTop, behavior: 'smooth' });
                    $('#textArea-chat-message').val('');
                    $('#ul-chat-attachments').html('');
                    _this.voiceRecognizer.translate(msg, { "lang": lang });
                });
            }
        });
    };
    app.prototype.addToChatWindow = function (textPage, userFirstName) {
        var _this = this;
        return new Promise(function (resolve, reject) {
            var _a;
            try {
                var li = document.createElement('li');
                (_a = li.classList).add.apply(_a, ['d-flex', 'justify-content-between', 'mb-2', 'direct-chat-msg']);
                li.innerHTML = "<img src=\"".concat(_this.profilePicture, "\" alt=\"avatar\"\n                                                     class=\"rounded-circle d-flex align-self-start me-3 shadow-1-strong\" width=\"60\">\n                                                <div class=\"card w-100\">\n                                                    <div class=\"card-header d-flex justify-content-between\">\n                                                        <p class=\"fw-bold mb-0\">").concat(userFirstName, "</p>\n                                                        <p class=\"text-muted small mb-0\"><i class=\"far fa-clock\"></i> ").concat(moment().format("D MMM h:mm a"), "</p>\n                                                    </div>\n                                                    <div class=\"card-body\">\n                                                        <p class=\"mb-0\">\n                                                            ").concat(textPage, "\n                                                        </p>\n                                                    </div>\n                                                </div>");
                var msg_li = document.getElementsByClassName('list-unstyled custom-scrollbar').item(0).appendChild(li);
                return resolve(msg_li);
            }
            catch (e) {
                return reject();
            }
        });
    };
    app.prototype.addToChatBox = function (textPage) {
        $('#textArea-chat-message').val(textPage).trigger('focus');
        //tinymce.activeEditor.setContent(`<p>${textPage}</p>`);
    };
    app.prototype.addToAttachments = function (file) {
        return new Promise(function (resolve, reject) {
            try {
                var filePath = URL.createObjectURL(file);
                var audio_1 = new Audio(filePath);
                audio_1.preload = "metadata";
                audio_1.addEventListener('loadedmetadata', function () {
                    return resolve({ "filename": file.name, "audioduration": audio_1.duration, "audiosrc": audio_1.src });
                });
            }
            catch (e) {
                reject(e);
            }
        });
    };
    app.prototype.transcript = function (file) {
        var _this = this;
        var formdata = new FormData();
        formdata.append("file", file);
        formdata.append("model", "whisper-1");
        document.getElementById('progress-pdf-parser').style.width = '0%';
        document.getElementById('progress-pdf-parser').setAttribute('aria-valuenow', '0');
        return $.ajax({
            type: 'POST',
            url: '/openai/transcript',
            processData: false,
            contentType: false,
            data: formdata,
            xhr: function () {
                var xhr = new window.XMLHttpRequest();
                //Upload progress
                xhr.upload.addEventListener("progress", function (evt) {
                    if (evt.lengthComputable) {
                        var percentComplete = evt.loaded / evt.total;
                        var percent_loaded = Math.ceil(percentComplete) * 100;
                        document.getElementById('progress-pdf-parser').style.width = "".concat(percent_loaded, "%");
                        document.getElementById('progress-pdf-parser').setAttribute('aria-valuenow', percent_loaded.toFixed(2));
                        console.log(percentComplete);
                    }
                }, false);
                //Download progress
                xhr.addEventListener("progress", function (evt) {
                    if (evt.lengthComputable) {
                        var percentComplete = evt.loaded / evt.total;
                        //Do something with download progress
                        console.log(percentComplete);
                    }
                }, false);
                return xhr;
            }
        }).then(function (response, textStatus, xhr) {
            if (xhr.status === 200) {
                var msg = response;
                return msg;
            }
        }).fail(function (XMLHttpRequest, textStatus, errorThrown) {
            _this.errMngr.logAjaxError(XMLHttpRequest, textStatus, errorThrown);
        });
    };
    app.prototype.parsePdf = function (filepath) {
        var _this = this;
        return new Promise(function (resolve, reject) {
            try {
                PDFJS.getDocument(filepath).promise.then(function (PDFDocumentInstance) {
                    // Use the PDFDocumentInstance To extract the text later
                    var totalPages = PDFDocumentInstance.numPages;
                    var pageNumber = 1;
                    // Extract the text
                    _this.getPageText(pageNumber, PDFDocumentInstance).then(function (textPage) {
                        // Show the text of the page in the console
                        return resolve(textPage);
                    });
                }, function (reason) {
                    // PDF loading error
                    return reject(reason);
                });
            }
            catch (e) {
                reject(e);
            }
        });
    };
    /**
     * Retrieves the text of a specif page within a PDF Document obtained through pdf.js
     *
     * @param {Integer} pageNum Specifies the number of the page
     * @param {PDFDocument} PDFDocumentInstance The PDF document obtained
     **/
    app.prototype.getPageText = function (pageNum, PDFDocumentInstance) {
        // Return a Promise that is solved once the text of the page is retrieven
        return new Promise(function (resolve, reject) {
            PDFDocumentInstance.getPage(pageNum).then(function (pdfPage) {
                // The main trick to obtain the text of the PDF page, use the getTextContent method
                pdfPage.getTextContent().then(function (textContent) {
                    var textItems = textContent.items;
                    var finalString = "";
                    document.getElementById('progress-pdf-parser').style.width = '0%';
                    document.getElementById('progress-pdf-parser').setAttribute('aria-valuenow', '0');
                    // Concatenate the string of the item to the final string
                    for (var i = 0; i < textItems.length; i++) {
                        var item = textItems[i];
                        finalString += item.str + " ";
                        var percent_loaded = Math.ceil((i / (textItems.length - 1)) * 100);
                        document.getElementById('progress-pdf-parser').style.width = "".concat(percent_loaded, "%");
                        document.getElementById('progress-pdf-parser').setAttribute('aria-valuenow', percent_loaded.toFixed(2));
                        console.log(percent_loaded);
                    }
                    // Solve promise with the text retrieven from the page
                    return resolve(finalString);
                });
            });
        });
    };
    return app;
}());
exports.default = app;
var VoiceRecognizer = /** @class */ (function (_super) {
    __extends(VoiceRecognizer, _super);
    function VoiceRecognizer(userFirstName, profilePicture) {
        var _this = _super.call(this) || this;
        _this.userFirstName = userFirstName;
        _this.profilePicture = profilePicture;
        _this.language = "en-GB";
        _this.errMngr = new error_manager_1.default();
        _this.grammar = '#JSGF V1.0; grammar colors; public <color> = aqua | azure | beige | bisque | black | blue | brown | chocolate | coral | crimson | cyan | fuchsia | ghostwhite | gold | goldenrod | gray | green | indigo | ivory | khaki | lavender | lime | linen | magenta | maroon | moccasin | navy | olive | orange | orchid | peru | pink | plum | purple | red | salmon | sienna | silver | snow | tan | teal | thistle | tomato | turquoise | violet | white | yellow ;';
        _this.diagnostic = document.getElementsByClassName('list-unstyled custom-scrollbar').item(0);
        _this.recognition = new webkitSpeechRecognition() || new SpeechRecognition();
        _this.speechRecognitionList = new webkitSpeechGrammarList() || new SpeechGrammarList();
        _this.speechRecognitionList.addFromString(_this.grammar, 1);
        _this.recognition.grammars = _this.speechRecognitionList;
        _this.recognition.continuous = true;
        _this.recognition.lang = _this.language;
        _this.recognition.interimResults = false;
        _this.recognition.maxAlternatives = 1;
        var synth = speechSynthesis;
        _this.voices = synth.getVoices();
        speechSynthesis.onvoiceschanged = function () {
            _this.voices = speechSynthesis.getVoices();
            //console.log(...voices);
            var langs = Array.from(new Set(_this.voices.map(function (voice) { return voice.lang; })));
            langs.sort();
            $('#select-translation-language').val('').multiselect({
                nonSelectedText: 'Please select language',
                disableIfEmpty: true,
                buttonClass: 'btn btn-primary',
                buttonWidth: '100%',
                maxHeight: 250,
                selectedClass: 'active multiselect-selected',
                includeSelectAllOption: false,
                buttonContainer: '<div class="multiselect-buttons btn-group d-flex w-100"></div>',
                templates: {
                    button: "<button type=\"button\" class=\"multiselect dropdown-bordered dropdown-toggle dropdown-toggle-split\" data-mdb-toggle=\"dropdown\">\n                                <span class=\"multiselect-selected-text\"> </span>\n                             </button>",
                    ul: '<ul class="multiselect-container dropdown-menu custom-scrollbar w-100" ></ul>',
                    li: "<li>\n                            <a class=\"dropdown-item\">\n                                <label class=\"radio\">\n                                <input class=\"preview-subject ellipsis font-weight-medium text-dark\"></label>\n                            </a>\n                         </li>"
                },
                onChange: function (option, checked) {
                    _this.language = option.html();
                    _this.recognition.lang = _this.language;
                    //Microsoft Libby Online (Natural) - English (United Kingdom)
                    //Microsoft Salma Online (Natural) - Arabic (Egypt)
                    _this.voice = _this.getVoice(_this.voices, _this.language);
                }
            });
            $('#select-languages').val('').multiselect({
                nonSelectedText: 'Please select language',
                disableIfEmpty: true,
                buttonClass: 'btn btn-success d-inline-block',
                buttonWidth: '100%',
                maxHeight: 450,
                selectedClass: 'active multiselect-selected',
                includeSelectAllOption: false,
                buttonContainer: '<div class="multiselect-buttons btn-group d-flex w-100"></div>',
                templates: {
                    button: "<button type=\"button\" class=\"multiselect dropdown-bordered dropdown-toggle dropdown-toggle-split\" data-mdb-toggle=\"dropdown\">\n                                <span class=\"multiselect-selected-text\"> </span>\n                             </button>",
                    ul: '<ul class="multiselect-container dropdown-menu custom-scrollbar" style="min-width:175px;"></ul>',
                    li: "<li>\n                            <a class=\"dropdown-item\">\n                                <label class=\"radio\">\n                                <input class=\"preview-subject ellipsis font-weight-medium text-dark\"></label>\n                            </a>\n                         </li>"
                },
                onChange: function (option, checked) {
                    _this.language = option.html();
                    _this.recognition.lang = _this.language;
                    _this.voice = _this.getVoice(_this.voices, _this.language);
                }
            });
            var options = [];
            langs.forEach(function (lang, index) {
                options.push({ label: lang, title: lang, value: index, selected: lang === _this.language });
            });
            $('#select-translation-language').multiselect('dataprovider', options);
            $('#select-translation-language').multiselect('rebuild');
            $('#select-languages').multiselect('dataprovider', options);
            $('#select-languages').multiselect('rebuild');
            console.log(_this.voices);
            if (!_this.voice) {
                console.log($('#select-languages option:selected').text());
                _this.voice = _this.getVoice(_this.voices, _this.language);
            }
        };
        _this.recognition.onresult = function (event) {
            var results = event.results;
            //const msg = results.item(results.length - 1)[0].transcript;
            for (var _i = 0, _a = Array.from(event.results); _i < _a.length; _i++) {
                var result = _a[_i];
                // Print the transcription to the console
                var msg = result[0].transcript;
                _this.diagnostic.innerHTML += "<li class=\"d-flex justify-content-between mb-2 direct-chat-msg\" dir=\"auto\">\n                                                <img src=\"".concat(profilePicture, "\" alt=\"avatar\"\n                                                     class=\"rounded-circle d-flex align-self-start me-3 shadow-1-strong\" width=\"60\">\n                                                <div class=\"card w-100\">\n                                                    <div class=\"card-header d-flex justify-content-between\">\n                                                        <p class=\"fw-bold mb-0\">").concat(userFirstName, "</p>\n                                                        <p class=\"text-muted small mb-0\"><i class=\"far fa-clock\"></i> ").concat(moment().format("D MMM h:mm a"), "</p>\n                                                    </div>\n                                                    <div class=\"card-body\">\n                                                        <p class=\"mb-0\">\n                                                            ").concat(msg, "\n                                                        </p>\n                                                    </div>\n                                                </div>\n                                            </li>");
                var lastMsg = document.getElementsByClassName('direct-chat-msg');
                _this.diagnostic.scrollTo({ top: lastMsg.item(lastMsg.length - 1).offsetTop, behavior: 'smooth' });
                if (msg.toLowerCase().includes('draw') || msg.toLowerCase().includes('paint') || msg.toLowerCase().includes('sketch') || msg.toLowerCase().includes('portray') || msg.toLowerCase().includes('plot')) {
                    _this.draw(msg);
                }
                else {
                    var lang = $('#select-languages option').filter(':selected').text();
                    _this.chat("".concat(msg), { "lang": lang });
                }
            }
        };
        _this.conversation = [{ "role": "user", "content": "Good Morning, my name is ".concat(userFirstName, ".") },
            { "role": "assistant", "content": "Good morning ".concat(userFirstName, ", How may I assest you today?") },
            {
                "role": "user", "content": "What is your name?"
            },
            { "role": "assistant", "content": "My Name is Lucy." }, {
                "role": "user", "content": "Hello Lucy."
            },
            { "role": "assistant", "content": "Hello ".concat(userFirstName, ".") }];
        return _this;
    }
    VoiceRecognizer.prototype.getVoice = function (voices, languageCode) {
        var voice;
        try {
            if (languageCode.startsWith('en')) {
                //Microsoft Libby Online (Natural) - English (United Kingdom)
                //Microsoft Salma Online (Natural) - Arabic (Egypt)
                voice = voices.filter(function (voice) { return voice.lang.startsWith('en') && voice.name.includes('Libby'); })[0];
                console.log(voice.name);
            }
            else if (languageCode.startsWith('ar')) {
                voice = voices.filter(function (voice) { return voice.lang.startsWith('ar') && voice.name.includes('Salma'); })[0];
                console.log(voice.name);
            }
            else if (languageCode.startsWith('es')) {
                voice = voices.filter(function (voice) { return voice.lang.startsWith('es') && voice.name.includes('Elvira'); })[0];
                console.log(voice.name);
            }
            else if (languageCode.startsWith('hi')) {
                voice = voices.filter(function (voice) { return voice.lang.startsWith('hi') && voice.name.includes('Swara'); })[0];
                console.log(voice.name);
            }
            else if (languageCode.startsWith('it')) {
                voice = voices.filter(function (voice) { return voice.lang.startsWith('it') && voice.name.includes('Elsa'); })[0];
                console.log(voice.name);
            }
            else if (languageCode.startsWith('nl')) {
                voice = voices.filter(function (voice) { return voice.lang.startsWith('nl') && voice.name.includes('Colette'); })[0];
                console.log(voice.name);
            }
            else if (languageCode.startsWith('ja')) {
                voice = voices.filter(function (voice) { return voice.lang.startsWith('ja') && voice.name.includes('Nanami'); })[0];
                console.log(voice.name);
            }
            else {
                voice = voices.filter(function (voice) { return voice.lang.includes(languageCode); })[0];
                console.log(voice.name);
            }
        }
        catch (e) {
            console.error(e);
            voice = voices.filter(function (voice) { return voice.lang.includes(languageCode); })[0];
            console.log(voice.name);
        }
        return voice;
    };
    VoiceRecognizer.prototype.start = function () {
        this.recognition.start();
    };
    VoiceRecognizer.prototype.stop = function () {
        this.recognition.stop();
    };
    VoiceRecognizer.prototype.chat = function (prompt, options) {
        var _this = this;
        if (prompt && prompt !== '') {
            this.conversation.push({ "role": "user", "content": prompt });
        }
        return $.ajax({
            type: 'POST',
            url: '/openai/chat',
            dataType: 'json',
            data: {
                "model": "gpt-3.5-turbo",
                "messages": JSON.stringify(this.conversation)
            }
        }).then(function (response, textStatus, xhr) {
            if (xhr.status === 200) {
                var msg = response;
                _this.conversation.push({ "role": "assistant", "content": msg });
                var li = $("<li class=\"d-flex justify-content-between mb-2 direct-chat-msg pull-right\" dir=\"auto\">\n                                                <div class=\"card w-100\">\n                                                    <div class=\"card-header d-flex justify-content-between\">\n                                                        <p class=\"fw-bold mb-0\">Lucy</p>\n                                                        <a class=\"btn btn-sm btn-link ripple-surface btn-floating btn-read\" data-mdb-toggle=\"collapse\" href=\"#\" role=\"button\" aria-expanded=\"false\" aria-controls=\"read\" data-ripple-color=\"hsl(0, 0%, 67%)\" style=\"\">\n                                                            <span class=\"material-icons md-18\">record_voice_over</span>\n                                                        </a>\n                                                        <p class=\"text-muted small mb-0\"><i class=\"far fa-clock\"></i> ".concat(moment().format("D MMM h:mm a"), "</p>\n                                                    </div>\n                                                    <div class=\"card-body\">\n                                                        <p class=\"mb-0\" dir=\"auto\">\n                                                             ").concat(msg, "\n                                                        </p>\n                                                    </div>\n                                                </div>\n                                                <img src=\"/img/Lucy.png\" alt=\"avatar\"\n                                                     class=\"rounded-circle d-flex align-self-start ms-3 shadow-1-strong\" width=\"60\">\n                                            </li>"));
                _this.diagnostic.appendChild(li.get(0));
                var lastMsg = document.getElementsByClassName('direct-chat-msg');
                _this.diagnostic.scrollTo({ top: lastMsg.item(lastMsg.length - 1).offsetTop, behavior: 'smooth' });
                li.find('.btn-read').on('click', function (event) {
                    event.preventDefault();
                    var current = event.currentTarget;
                    var message = $(current).closest('.card').find('.card-body p').text();
                    _this.voice = _this.getVoice(_this.voices, options.lang);
                    _this.speak(message, { "language": options.lang });
                });
                _this.speak(msg, { "language": options.lang });
            }
        }).fail(function (XMLHttpRequest, textStatus, errorThrown) {
            _this.errMngr.logAjaxError(XMLHttpRequest, textStatus, errorThrown);
        });
    };
    VoiceRecognizer.prototype.draw = function (prompt, options) {
        var _this = this;
        return $.ajax({
            type: 'POST',
            url: '/openai/draw',
            dataType: 'json',
            data: { "prompt": "".concat(prompt), "n": "1", "size": "1024x1024" }
        }).then(function (response, textStatus, xhr) {
            if (xhr.status === 200) {
                _this.diagnostic.innerHTML += "<li class=\"d-flex justify-content-between mb-2 direct-chat-msg pull-right\">\n                                                <div class=\"card w-100\">\n                                                    <div class=\"card-header d-flex justify-content-between\">\n                                                        <p class=\"fw-bold mb-0\">Lucy</p>\n                                                        <p class=\"text-muted small mb-0\"><i class=\"far fa-clock\"></i> ".concat(moment().format("D MMM h:mm a"), "</p>\n                                                    </div>\n                                                    <div class=\"card-body\">\n                                                        <div class=\"canvas-imagine\" style=\"display: block; min-height: 250px;\">\n                                                        </div>\n                                                    </div>\n                                                </div>\n                                                <img src=\"/img/Lucy.png\" alt=\"avatar\"\n                                                     class=\"rounded-circle d-flex align-self-start ms-3 shadow-1-strong\" width=\"60\">\n                                            </li>");
                var canvases = document.getElementsByClassName('canvas-imagine');
                var canvas = canvases.item(canvases.length - 1);
                canvas.style.background = "url(".concat(response, ")");
                canvas.style.backgroundSize = 'contain';
                canvas.style.backgroundRepeat = 'no-repeat';
                canvas.style.backgroundPosition = 'center';
                var lastMsg = document.getElementsByClassName('direct-chat-msg');
                _this.diagnostic.scrollTo({ top: lastMsg.item(lastMsg.length - 1).offsetTop, behavior: 'smooth' });
            }
        }).fail(function (XMLHttpRequest, textStatus, errorThrown) {
            _this.errMngr.logAjaxError(XMLHttpRequest, textStatus, errorThrown);
        });
    };
    VoiceRecognizer.prototype.translate = function (prompt, options) {
        var _this = this;
        if (prompt && prompt !== '') {
            this.conversation.push({
                "role": "user", "content": "Translate this into ".concat(options.lang, ": \"").concat(prompt, "\", and don't include the source text.'\n                                            Return only the equivalent html code for the translation, separate each phrase in span tag with lang attribute and the direction attribute that match its recognized language based on context and narrative flow.\n                                            Incluse the tags in div element with class named \"translation-result\" and add a class \"text-end\" to the div if the translation language is written from right to left.")
            });
        }
        return $.ajax({
            type: 'POST',
            url: '/openai/translate',
            dataType: 'json',
            data: {
                "model": "gpt-3.5-turbo",
                "messages": JSON.stringify(this.conversation)
            }
        }).then(function (response, textStatus, xhr) {
            if (xhr.status === 200) {
                var msg = response;
                _this.conversation.push({ "role": "assistant", "content": msg });
                var li = $("<li class=\"d-flex justify-content-between mb-2 direct-chat-msg pull-right\" dir=\"auto\">\n                                <div class=\"card w-100\">\n                                    <div class=\"card-header d-flex justify-content-between\">\n                                        <p class=\"fw-bold mb-0\">Lucy</p>\n                                        <a class=\"btn btn-sm btn-link ripple-surface btn-floating btn-read\" data-mdb-toggle=\"collapse\" href=\"#\" role=\"button\" aria-expanded=\"false\" aria-controls=\"read\" data-ripple-color=\"hsl(0, 0%, 67%)\" style=\"\">\n                                            <span class=\"material-icons md-18\">record_voice_over</span>\n                                        </a>\n                                        <p class=\"text-muted small mb-0\"><i class=\"far fa-clock\"></i> ".concat(moment().format("D MMM h:mm a"), "</p>\n                                    </div>\n                                    <div class=\"card-body\">\n                                        ").concat(msg, "\n                                    </div>\n                                </div>\n                                <img src=\"/img/Lucy.png\" alt=\"avatar\"\n                                        class=\"rounded-circle d-flex align-self-start ms-3 shadow-1-strong\" width=\"60\">\n                            </li>"));
                _this.diagnostic.appendChild(li.get(0));
                var lastMsg = document.getElementsByClassName('direct-chat-msg');
                _this.diagnostic.scrollTo({ top: lastMsg.item(lastMsg.length - 1).offsetTop, behavior: 'smooth' });
                li.find('.btn-read').on('click', function (event) {
                    event.preventDefault();
                    var current = event.currentTarget;
                    var message = $(current).closest('.card').find('.card-body .translation-result span').text();
                    _this.voice = _this.getVoice(_this.voices, options.lang);
                    _this.speak(message, { "language": options.lang });
                });
                var translation = $(msg).find('span');
                $.each(translation, function (index, p) { return __awaiter(_this, void 0, void 0, function () {
                    return __generator(this, function (_a) {
                        switch (_a.label) {
                            case 0: return [4 /*yield*/, this.speak(p.innerHTML, { "language": p.lang })];
                            case 1:
                                _a.sent();
                                return [2 /*return*/];
                        }
                    });
                }); });
            }
        }).fail(function (XMLHttpRequest, textStatus, errorThrown) {
            _this.errMngr.logAjaxError(XMLHttpRequest, textStatus, errorThrown);
        });
    };
    VoiceRecognizer.prototype.speak = function (msg, options) {
        // https://jsfiddle.net/ourcodeworld/9k0z6m14/4/
        var _this = this;
        return new Promise(function (resolve, reject) {
            try {
                var utterance = new SpeechSynthesisUtterance(msg);
                utterance.lang = options.language;
                utterance.voice = _this.voice;
                utterance.rate = 1;
                utterance.pitch = 1;
                utterance.volume = 0.5;
                utterance.onend = function (event) {
                    try {
                        if ($('#flexSwitchCheckChecked').is(':checked')) {
                            _this.recognition.start();
                        }
                        else {
                            _this.recognition.stop();
                        }
                    }
                    catch (e) {
                        console.log(e);
                    }
                    return resolve('complete');
                };
                utterance.onstart = function (event) {
                    console.log(event.currentTarget);
                    navigator.mediaDevices.enumerateDevices()
                        // set `getUserMedia()` constraints to "auidooutput", where avaialable
                        // see https://bugzilla.mozilla.org/show_bug.cgi?id=934425, https://stackoverflow.com/q/33761770
                        .then(function (devices) {
                        var audiooutput = devices.find(function (device) { return device.kind === "audiooutput" && device.deviceId === "default"; });
                        var label = audiooutput.label.replace('Default - ', '');
                        audiooutput = devices.find(function (device) { return device.kind === "audiooutput" && device.label === label; });
                        if (audiooutput) {
                            var constraints = {
                                audio: {
                                    deviceId: { exact: audiooutput.deviceId },
                                    groupId: audiooutput.groupId
                                }
                            };
                            navigator.mediaDevices.getUserMedia(constraints).then(function (stream) {
                                console.log(stream.getAudioTracks()[0].label);
                                var equalizer = new Equalizer(_this.profilePicture, stream);
                                console.log('stream.active: ', stream.getAudioTracks().length);
                            }).catch(function (error) { return console.error(error); });
                        }
                    });
                };
                speechSynthesis.speak(utterance);
            }
            catch (e) {
                return reject(e);
            }
        });
    };
    return VoiceRecognizer;
}(events_1.EventEmitter));
var Equalizer = /** @class */ (function () {
    function Equalizer(profilePicture, stream) {
        var _this = this;
        this.profilePicture = profilePicture;
        //$(document).on('click', 'img', () => {
        this.errMngr = new error_manager_1.default();
        // Set up forked web audio context, for multiple browsers
        // window. is needed otherwise Safari explodes
        var audioCtx = new AudioContext();
        // Set up the different audio nodes we will use for the app
        var analyser = audioCtx.createAnalyser();
        var distortion = audioCtx.createWaveShaper();
        var gainNode = audioCtx.createGain();
        var biquadFilter = audioCtx.createBiquadFilter();
        var convolver = audioCtx.createConvolver();
        var echoDelay = this.createEchoDelayEffect(audioCtx);
        analyser.minDecibels = -90;
        analyser.maxDecibels = -10;
        analyser.smoothingTimeConstant = 0.85;
        if (stream === null || typeof stream === 'undefined') {
            // Main block for doing the audio recording
            if (navigator.mediaDevices.getUserMedia) {
                console.log("getUserMedia supported.");
                var constraints = { audio: true };
                this.initMediaDevices(constraints)
                    .then(function (stream) {
                    var source;
                    var tracks = stream.getAudioTracks();
                    console.log('tracks.length: ', tracks.length);
                    source = audioCtx.createMediaStreamSource(stream);
                    source.connect(gainNode);
                    gainNode.connect(analyser);
                    //analyser.connect(audioCtx.destination);
                    _this.visualize(analyser);
                })
                    .catch(function (err) {
                    console.log("The following gUM error occured: " + err);
                });
            }
            else {
                console.log("getUserMedia not supported on your browser!");
            }
        }
        else {
            var source = void 0;
            console.log(stream.getAudioTracks().length + ', ' + JSON.stringify(stream.getAudioTracks()[0].getConstraints().deviceId) + ', ' + stream.getAudioTracks()[0].kind + stream.getAudioTracks()[0].label + ', ' + stream.id);
            source = audioCtx.createMediaStreamSource(stream.clone());
            source.connect(gainNode);
            gainNode.connect(analyser);
            this.visualize(analyser);
        }
    }
    Equalizer.prototype.createEchoDelayEffect = function (audioContext) {
        var delay = audioContext.createDelay(1);
        var dryNode = audioContext.createGain();
        var wetNode = audioContext.createGain();
        var mixer = audioContext.createGain();
        var filter = audioContext.createBiquadFilter();
        delay.delayTime.value = 0.75;
        dryNode.gain.value = 1;
        wetNode.gain.value = 0;
        filter.frequency.value = 1100;
        filter.type = "highpass";
        return {
            apply: function () {
                wetNode.gain.setValueAtTime(0.75, audioContext.currentTime);
            },
            discard: function () {
                wetNode.gain.setValueAtTime(0, audioContext.currentTime);
            },
            isApplied: function () {
                return wetNode.gain.value > 0;
            },
            placeBetween: function (inputNode, outputNode) {
                inputNode.connect(delay);
                delay.connect(wetNode);
                wetNode.connect(filter);
                filter.connect(delay);
                inputNode.connect(dryNode);
                dryNode.connect(mixer);
                wetNode.connect(mixer);
                mixer.connect(outputNode);
            }
        };
    };
    Equalizer.prototype.visualize = function (analyser) {
        var visualSetting = "sinewave";
        console.log(visualSetting);
        if (visualSetting === "sinewave") {
            analyser.fftSize = 2048;
            var bufferLength_1 = analyser.fftSize;
            console.log(bufferLength_1);
            // We can use Float32Array instead of Uint8Array if we want higher precision
            // const dataArray = new Float32Array(bufferLength);
            var dataArray_1 = new Uint8Array(bufferLength_1);
            var render_1 = function () {
                // Set up canvas context for visualizer
                var canvas = document.querySelector(".canvas-visualizer");
                var canvasCtx = canvas.getContext("2d");
                var intendedWidth = $("#visualizer-container").innerWidth().toString();
                canvas.setAttribute("width", intendedWidth);
                var WIDTH = canvas.width;
                var HEIGHT = canvas.height;
                canvasCtx.clearRect(0, 0, WIDTH, HEIGHT);
                var drawVisual = requestAnimationFrame(render_1);
                analyser.getByteTimeDomainData(dataArray_1);
                canvasCtx.fillStyle = "rgba(255, 255, 255, 0)";
                canvasCtx.fillRect(0, 0, WIDTH, HEIGHT);
                canvasCtx.lineWidth = 1;
                canvasCtx.strokeStyle = "rgba(220, 53, 69, 1)";
                canvasCtx.beginPath();
                var sliceWidth = (WIDTH * 1.0) / bufferLength_1;
                var x = 0;
                for (var i = 0; i < bufferLength_1; i++) {
                    var v = dataArray_1[i] / 128.0;
                    var y = (v * HEIGHT) / 2;
                    if (i === 0) {
                        canvasCtx.moveTo(x, y);
                    }
                    else {
                        canvasCtx.lineTo(x, y);
                    }
                    x += sliceWidth;
                }
                canvasCtx.lineTo(canvas.width, canvas.height / 2);
                canvasCtx.stroke();
            };
            render_1();
        }
        else if (visualSetting == "frequencybars") {
            analyser.fftSize = 256;
            var bufferLengthAlt_1 = analyser.frequencyBinCount;
            console.log(bufferLengthAlt_1);
            // See comment above for Float32Array()
            var dataArrayAlt_1 = new Uint8Array(bufferLengthAlt_1);
            var drawAlt_1 = function () {
                // Set up canvas context for visualizer
                var canvas = document.querySelector(".canvas-visualizer");
                var canvasCtx = canvas.getContext("2d");
                var intendedWidth = document.getElementById("visualizer-container").clientWidth.toString();
                canvas.setAttribute("width", intendedWidth);
                var drawVisual;
                var WIDTH = canvas.width;
                var HEIGHT = canvas.height;
                canvasCtx.clearRect(0, 0, WIDTH, HEIGHT);
                drawVisual = requestAnimationFrame(drawAlt_1);
                analyser.getByteFrequencyData(dataArrayAlt_1);
                canvasCtx.fillStyle = "rgba(255, 255, 255, 1)";
                canvasCtx.fillRect(0, 0, WIDTH, HEIGHT);
                var barWidth = (WIDTH / bufferLengthAlt_1) * 2.5;
                var barHeight;
                var x = 0;
                for (var i = 0; i < bufferLengthAlt_1; i++) {
                    barHeight = dataArrayAlt_1[i];
                    canvasCtx.fillStyle = "rgb(" + (barHeight + 100) + ",50,50)";
                    canvasCtx.fillRect(x, HEIGHT - barHeight / 2, barWidth, barHeight / 2);
                    x += barWidth + 1;
                }
            };
            drawAlt_1();
        }
        else if (visualSetting == "off") {
            var canvas = document.querySelector(".canvas-visualizer");
            var canvasCtx = canvas.getContext("2d");
            var WIDTH = canvas.width;
            var HEIGHT = canvas.height;
            canvasCtx.clearRect(0, 0, WIDTH, HEIGHT);
            canvasCtx.fillStyle = "red";
            canvasCtx.fillRect(0, 0, WIDTH, HEIGHT);
        }
    };
    Equalizer.prototype.visualizeD3 = function (analyser) {
        //TODO: https://blog.scottlogic.com/2016/01/06/audio-api-with-d3.html
        // https://css-tricks.com/making-an-audio-waveform-visualizer-with-vanilla-javascript/
        // https://medium.com/swlh/visualizing-sound-with-d3-and-web-audio-api-435ffea88f30
        // https://github.com/willianjusten/awesome-audio-visualization
        var _this = this;
        var visualSetting = "sinewave";
        console.log(visualSetting);
        switch (visualSetting) {
            case "sinewave":
                {
                    analyser.fftSize = 2048;
                    var bufferLength = analyser.fftSize;
                    //console.log(bufferLength);
                    // We can use Float32Array instead of Uint8Array if we want higher precision
                    //const dataArray = new Float32Array(bufferLength);
                    //const bufferLength = analyser.frequencyBinCount;
                    var dataArray_2 = new Uint8Array(bufferLength);
                    var render_2 = function () {
                        var drawVisual = requestAnimationFrame(render_2);
                        analyser.getByteFrequencyData(dataArray_2);
                    };
                    render_2();
                }
                break;
            case "rounded-sinewave":
                {
                    analyser.fftSize = 2048;
                    var bufferLength = analyser.fftSize;
                    //console.log(bufferLength);
                    // We can use Float32Array instead of Uint8Array if we want higher precision
                    //const dataArray = new Float32Array(bufferLength);
                    //const bufferLength = analyser.frequencyBinCount;
                    var dataArray_3 = new Uint8Array(bufferLength);
                    var render_3 = function () {
                        var drawVisual = requestAnimationFrame(render_3);
                        //analyser.getByteFrequencyData(dataArray);
                        analyser.getByteFrequencyData(dataArray_3);
                        //console.log(dataArray.length);
                        var svg = d3.select('.svg-visualizer');
                        svg.attr('background-color', 'white');
                        svg.selectAll('*').remove();
                        var margin = {
                            top: 0,
                            right: 0,
                            bottom: 0,
                            left: 0
                        };
                        var width = +svg.attr('width') - margin.left - margin.right;
                        var height = +svg.attr('height') - margin.top - margin.bottom;
                        // content area of your visualization
                        var vis = svg.append('g')
                            .attr('transform', "translate(".concat(margin.left + width / 2, ",").concat(margin.top + height / 2, ")"));
                        // show scales
                        var xScale = d3.scaleLinear()
                            .domain([-128, 128])
                            .range([-width / 2, width / 2]);
                        // draw circle
                        var radius = 85;
                        var length = 256; //64;
                        var amplitude = 5;
                        var radialGenerator = d3.lineRadial()
                            .angle(function (d) { return d.angle; })
                            .radius(function (d) { return d.radius; })
                            .curve(d3.curveCardinalClosed);
                        var radialScale = d3.scaleLinear()
                            .domain([0, length])
                            .range([0, Math.PI * 2]);
                        var data = d3.range(length).map(function (d, i) {
                            return {
                                angle: radialScale(d),
                                radius: xScale(radius) + (dataArray_3[i] / 128.0) * amplitude
                            };
                        });
                        var wave = vis.append('path')
                            .attr('d', radialGenerator(data))
                            .attr('fill', '#ffffff')
                            .attr('stroke', '#9575CD')
                            .attr('stroke-width', '2px');
                        var defs = svg.append("defs").attr("id", "imgdefs");
                        var catpattern = defs.append("pattern")
                            .attr("id", "catpattern")
                            .attr("height", 1)
                            .attr("width", 1)
                            .attr("x", "0")
                            .attr("y", "0");
                        //https://stackoverflow.com/questions/20660085/how-to-stretch-an-image-in-a-svg-shape-to-fill-its-bounds
                        catpattern.append("image")
                            .attr("height", 70)
                            .attr("width", 70)
                            .attr("xlink:href", function () { return _this.profilePicture; })
                            .attr("preserveAspectRatio", "xMidYMid slice");
                        vis.append("circle")
                            .attr("r", 35)
                            .attr("cy", 0)
                            .attr("cx", 0)
                            .attr('stroke', '#9575CD')
                            .attr('stroke-width', '3px')
                            .attr("fill", "url(#catpattern)");
                        //Mask approach
                        //https://codepen.io/tylersticka/pen/NWWqPmQ
                    };
                    render_3();
                }
                break;
            default:
        }
    };
    Equalizer.prototype.initMediaDevices = function (constraints) {
        return new Promise(function (resolve, reject) {
            if (!navigator.mediaDevices.getUserMedia || navigator.mediaDevices === undefined || navigator.mediaDevices.getUserMedia === undefined) {
                reject(new Error("getUserMedia is not implemented in this browser"));
            }
            else {
                // Otherwise, wrap the call to the old navigator.getUserMedia with a Promise
                return resolve(navigator.mediaDevices.getUserMedia(constraints));
            }
        });
    };
    Equalizer.prototype.voiceChange = function (distortion, biquadFilter, audioCtx, echoDelay, gainNode, convolver) {
        distortion.oversample = "4x";
        biquadFilter.gain.setTargetAtTime(0, audioCtx.currentTime, 0);
        var voiceSetting = "off";
        console.log(voiceSetting);
        if (echoDelay.isApplied()) {
            echoDelay.discard();
        }
        // When convolver is selected it is connected back into the audio path
        if (voiceSetting == "convolver") {
            biquadFilter.disconnect(0);
            biquadFilter.connect(convolver);
        }
        else {
            biquadFilter.disconnect(0);
            biquadFilter.connect(gainNode);
            if (voiceSetting == "distortion") {
                distortion.curve = this.makeDistortionCurve(400);
            }
            else if (voiceSetting == "biquad") {
                biquadFilter.type = "lowshelf";
                biquadFilter.frequency.setTargetAtTime(1000, audioCtx.currentTime, 0);
                biquadFilter.gain.setTargetAtTime(25, audioCtx.currentTime, 0);
            }
            else if (voiceSetting == "delay") {
                echoDelay.apply();
            }
            else if (voiceSetting == "off") {
                console.log("Voice settings turned off");
            }
        }
    };
    // Distortion curve for the waveshaper, thanks to Kevin Ennis
    // http://stackoverflow.com/questions/22312841/waveshaper-node-in-webaudio-how-to-emulate-distortion
    Equalizer.prototype.makeDistortionCurve = function (amount) {
        var k = typeof amount === "number" ? amount : 50, n_samples = 44100, curve = new Float32Array(n_samples), deg = Math.PI / 180, i = 0, x;
        for (; i < n_samples; ++i) {
            x = (i * 2) / n_samples - 1;
            curve[i] = ((3 + k) * x * 20 * deg) / (Math.PI + k * Math.abs(x));
        }
        return curve;
    };
    return Equalizer;
}());
//# sourceMappingURL=app.js.map