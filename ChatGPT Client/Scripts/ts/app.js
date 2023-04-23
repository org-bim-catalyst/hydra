"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
var PDFJS = require("pdfjs-dist/webpack");
var d3 = require("d3");
var $ = require("jquery");
require("bootstrap-multiselect");
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
var app = /** @class */ (function () {
    function app(userFirstName, profilePicture) {
        var _this = this;
        this.userFirstName = userFirstName;
        this.profilePicture = profilePicture;
        var welcomeMsg = "<div class=\"modal fade show\" id=\"exampleModal\" tabindex=\"-1\" aria-labelledby=\"exampleModalLabel\" aria-modal=\"true\" role=\"dialog\" style=\"display: block;\">\n                             <div class=\"modal-dialog modal-dialog-centered\">\n                                <div class=\"modal-content\">\n                                  <div class=\"modal-header\">\n                                    <h3>Welcome ".concat(userFirstName, "</h3>\n                                    <img src=\"/img/Lucy.png\" class=\"rounded-circle shadow-1-strong\" width=\"85\" height=\"85\" alt=\"\" aria-controls=\"#picker-editor\" >\n                                  </div>\n                                  <div class=\"modal-body\">\n                                      <div class=\"d-flex justify-content-end align-items-end\">\n                                       <img src=\"/img/edge-logo.webp\" class=\"rounded me-1\" width=\"100\" height=\"100\" alt=\"\" aria-controls=\"#picker-editor\">\n                                       <p class=\"lead\">For better experience, we recommend you to use Microsoft Edge.</p>\n\n                                      </div>\n                                  </div>\n                                  <div class=\"modal-footer\">\n                                    <button type=\"button\" class=\"btn btn-secondary\" data-mdb-dismiss=\"modal\">OK</button>\n                                  </div>\n                                </div>\n                              </div>\n                              </div>");
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
                    _this.tanscript(file).then(function (textPage) {
                        _this.addToChatBox(textPage);
                        _this.addToAttachments(file);
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
        //// create an array of options
        //const options = [
        //    { value: 'apple', label: 'Apple' },
        //    { value: 'banana', label: 'Banana' },
        //    { value: 'orange', label: 'Orange' },
        //];
        var _this = this;
        //// create a select element using mdb.Select component
        //const selectElement = new Select(document.getElementById('mySelect'), {
        //    options: options,
        //    clearable: true,
        //    search: true,
        //    placeholder: 'Select a fruit',
        //});
        //Tags.init("#tags-input", { maximumItems: 1, clearEnd: true });
        this.voiceRecognizer = new VoiceRecognizer(this.userFirstName, this.profilePicture);
        this.equalizer = new Equalizer(this.profilePicture);
        $('#button-send-message').on('click', function (event) {
            event.preventDefault();
            var msg = $('#textArea-chat-message').val().toString();
            //let msg = tinymce.activeEditor.getContent();
            _this.addToChatWindow(msg, _this.userFirstName).then(function () {
                var diagnostic = document.getElementsByClassName('list-unstyled custom-scrollbar').item(0);
                var lastMsg = document.getElementsByClassName('direct-chat-msg');
                diagnostic.scrollTo({ top: lastMsg.item(lastMsg.length - 1).offsetTop, behavior: 'smooth' });
                $('#textArea-chat-message').val('');
                $('#ul-chat-attachments').html('');
                //tinymce.activeEditor.setContent('');
                if (msg.toLowerCase().includes('draw') || msg.toLowerCase().includes('paint') || msg.toLowerCase().includes('sketch') || msg.toLowerCase().includes('portray') || msg.toLowerCase().includes('plot')) {
                    _this.voiceRecognizer.draw(msg);
                }
                else if (msg.toLowerCase().includes('tanscript')) {
                    //this.voiceRecognizer.Tanscript(msg);
                }
                else {
                    _this.voiceRecognizer.chat(msg);
                }
            });
        });
        $("#mute").on('click', function (event) {
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
                resolve(msg_li);
            }
            catch (e) {
                reject();
            }
        });
    };
    app.prototype.addToChatBox = function (textPage) {
        $('#textArea-chat-message').val(textPage).trigger('focus');
        //tinymce.activeEditor.setContent(`<p>${textPage}</p>`);
    };
    app.prototype.addToAttachments = function (file) {
        var filePath = URL.createObjectURL(file);
        var audio = new Audio(filePath);
        audio.preload = "metadata";
        audio.addEventListener('loadedmetadata', function () {
            $('#ul-chat-attachments').html("<li class=\"list-group-item\">\n                                                <div class=\"d-flex justify-content-between align-items-center\">\n                                                    <div class=\"fw-bold\">".concat(file.name, "</div>\n                                                    <span class=\"badge rounded-pill badge-success\">").concat(moment.utc(moment.duration(audio.duration, "seconds").asMilliseconds()).format("HH:mm:ss"), "</span>\n                                                </div>\n\n                                                <div class=\"text-muted\">\n                                                    <audio id=\"audio-data\" preload=\"auto\">\n                                                        <source src=\"").concat(audio.src, "\">\n                                                    </audio>\n                                                    <div id=\"audioplayer d-flex justify-content-between align-items-center\">\n                                                        <i id=\"pButton\" class=\"fas fa-play\"></i>\n                                                        <div id=\"timeline\">\n                                                            <div id=\"playhead\"></div>\n                                                        </div>\n                                                    </div>\n                                                </div>\n                                            </li>"));
        });
    };
    app.prototype.tanscript = function (file) {
        var formdata = new FormData();
        formdata.append("file", file);
        formdata.append("model", "whisper-1");
        document.getElementById('progress-pdf-parser').style.width = '0%';
        document.getElementById('progress-pdf-parser').setAttribute('aria-valuenow', '0');
        return $.ajax({
            type: 'POST',
            url: 'https://api.openai.com/v1/audio/transcriptions',
            processData: false,
            contentType: false,
            beforeSend: function (xhr) {
                xhr.setRequestHeader("Authorization", "Bearer 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");
            },
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
                console.log(JSON.stringify(response));
                var msg = response.text;
                return msg;
            }
        }).fail(function (XMLHttpRequest, textStatus, errorThrown) {
            console.log("Message: " + JSON.stringify(XMLHttpRequest));
            console.log("Status: " + textStatus);
            console.log("Error: " + errorThrown);
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
                        resolve(textPage);
                    });
                }, function (reason) {
                    // PDF loading error
                    reject(reason);
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
                    resolve(finalString);
                });
            });
        });
    };
    return app;
}());
exports.default = app;
var VoiceRecognizer = /** @class */ (function () {
    function VoiceRecognizer(userFirstName, profilePicture) {
        var _this = this;
        this.userFirstName = userFirstName;
        this.profilePicture = profilePicture;
        this.language = "en-GB";
        this.grammar = '#JSGF V1.0; grammar colors; public <color> = aqua | azure | beige | bisque | black | blue | brown | chocolate | coral | crimson | cyan | fuchsia | ghostwhite | gold | goldenrod | gray | green | indigo | ivory | khaki | lavender | lime | linen | magenta | maroon | moccasin | navy | olive | orange | orchid | peru | pink | plum | purple | red | salmon | sienna | silver | snow | tan | teal | thistle | tomato | turquoise | violet | white | yellow ;';
        this.diagnostic = document.getElementsByClassName('list-unstyled custom-scrollbar').item(0);
        this.recognition = new webkitSpeechRecognition() || new SpeechRecognition();
        this.speechRecognitionList = new webkitSpeechGrammarList() || new SpeechGrammarList();
        this.speechRecognitionList.addFromString(this.grammar, 1);
        this.recognition.grammars = this.speechRecognitionList;
        this.recognition.continuous = true;
        this.recognition.lang = this.language;
        this.recognition.interimResults = false;
        this.recognition.maxAlternatives = 1;
        var synth = speechSynthesis;
        var voices = synth.getVoices();
        speechSynthesis.onvoiceschanged = function () {
            voices = speechSynthesis.getVoices();
            console.log.apply(console, voices);
            var langs = Array.from(new Set(voices.map(function (voice) { return voice.lang; })));
            langs.sort();
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
                    if (_this.language === 'en-GB') {
                        //Microsoft Libby Online (Natural) - English (United Kingdom)
                        //Microsoft Salma Online (Natural) - Arabic (Egypt)
                        _this.voice = voices.filter(function (voice) { return voice.lang.includes(_this.language) && voice.name.includes('Libby'); })[0];
                        console.log(_this.voice.name);
                    }
                    else if (_this.language.startsWith('ar')) {
                        _this.voice = voices.filter(function (voice) { return voice.lang.includes(_this.language) && voice.name.includes('Salma'); })[0];
                        console.log(_this.voice.name);
                    }
                    else if (_this.language.startsWith('es')) {
                        _this.voice = voices.filter(function (voice) { return voice.lang.includes(_this.language) && voice.name.includes('Elvira'); })[0];
                        console.log(_this.voice.name);
                    }
                    else if (_this.language.startsWith('hi')) {
                        _this.voice = voices.filter(function (voice) { return voice.lang.includes(_this.language) && voice.name.includes('Swara'); })[0];
                        console.log(_this.voice.name);
                    }
                    else if (_this.language.startsWith('it')) {
                        _this.voice = voices.filter(function (voice) { return voice.lang.includes(_this.language) && voice.name.includes('Elsa'); })[0];
                        console.log(_this.voice.name);
                    }
                    else if (_this.language.startsWith('nl')) {
                        _this.voice = voices.filter(function (voice) { return voice.lang.includes(_this.language) && voice.name.includes('Colette'); })[0];
                        console.log(_this.voice.name);
                    }
                    else {
                        _this.voice = voices.filter(function (voice) { return voice.lang.includes(_this.language); })[0];
                        console.log(_this.voice.name);
                    }
                }
            });
            var options = [];
            langs.forEach(function (lang, index) {
                options.push({ label: lang, title: lang, value: index, selected: lang === _this.language });
            });
            $('#select-languages').multiselect('dataprovider', options);
            $('#select-languages').multiselect('rebuild');
            console.log(voices);
            if (!_this.voice) {
                console.log($('#select-languages option:selected').text());
                _this.voice = voices.filter(function (voice) { return voice.name.toLowerCase().includes('female'); })[0];
            }
        };
        this.recognition.onresult = function (event) {
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
                    _this.chat("".concat(msg, "\n"));
                }
            }
        };
        if ($('#flexSwitchCheckChecked').is(':checked')) {
            this.recognition.start();
        }
        else {
            this.recognition.stop();
        }
        this.conversation = [{ "role": "user", "content": "Good Morning, my name is ".concat(userFirstName, ".") },
            { "role": "assistant", "content": "Good morning ".concat(userFirstName, ", How may I assest you today?") },
            {
                "role": "user", "content": "What is your name?"
            },
            { "role": "assistant", "content": "My Name is Lucy." }, {
                "role": "user", "content": "Hello Lucy."
            },
            { "role": "assistant", "content": "Hello ".concat(userFirstName, ".") }];
    }
    VoiceRecognizer.prototype.start = function () {
        this.recognition.start();
    };
    VoiceRecognizer.prototype.stop = function () {
        this.recognition.stop();
    };
    VoiceRecognizer.prototype.chat = function (prompt) {
        var _this = this;
        if (prompt && prompt !== '') {
            this.conversation.push({ "role": "user", "content": prompt });
        }
        return $.ajax({
            type: 'POST',
            url: 'https://api.openai.com/v1/chat/completions',
            contentType: "application/json",
            beforeSend: function (xhr) {
                xhr.setRequestHeader("Authorization", "Bearer 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");
            },
            data: JSON.stringify({
                model: "gpt-3.5-turbo",
                messages: this.conversation
            })
        }).then(function (response, textStatus, xhr) {
            if (xhr.status === 200) {
                console.log(JSON.stringify(response));
                var msg = response.choices[0].message.content;
                _this.conversation.push({ "role": "assistant", "content": msg });
                _this.diagnostic.innerHTML += "<li class=\"d-flex justify-content-between mb-2 direct-chat-msg pull-right\" dir=\"auto\">\n                                                <div class=\"card w-100\">\n                                                    <div class=\"card-header d-flex justify-content-between\">\n                                                        <p class=\"fw-bold mb-0\">Lucy</p>\n                                                        <p class=\"text-muted small mb-0\"><i class=\"far fa-clock\"></i> ".concat(moment().format("D MMM h:mm a"), "</p>\n                                                    </div>\n                                                    <div class=\"card-body\">\n                                                        <p class=\"mb-0\">\n                                                             ").concat(msg, "\n                                                        </p>\n                                                    </div>\n                                                </div>\n                                                <img src=\"/img/Lucy.png\" alt=\"avatar\"\n                                                     class=\"rounded-circle d-flex align-self-start ms-3 shadow-1-strong\" width=\"60\">\n                                            </li>");
                var lastMsg = document.getElementsByClassName('direct-chat-msg');
                _this.diagnostic.scrollTo({ top: lastMsg.item(lastMsg.length - 1).offsetTop, behavior: 'smooth' });
                _this.speak(msg);
            }
        }).fail(function (XMLHttpRequest, textStatus, errorThrown) {
            console.log("Message: " + JSON.stringify(XMLHttpRequest));
            console.log("Status: " + textStatus);
            console.log("Error: " + errorThrown);
        });
    };
    VoiceRecognizer.prototype.draw = function (prompt) {
        var _this = this;
        return $.ajax({
            type: 'POST',
            url: 'https://api.openai.com/v1/images/generations',
            contentType: "application/json",
            beforeSend: function (xhr) {
                xhr.setRequestHeader("Authorization", "Bearer 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");
            },
            data: JSON.stringify({
                prompt: prompt,
                n: 1,
                size: "1024x1024"
            })
        }).then(function (response, textStatus, xhr) {
            if (xhr.status === 200) {
                console.log(JSON.stringify(response));
                _this.diagnostic.innerHTML += "<li class=\"d-flex justify-content-between mb-2 direct-chat-msg pull-right\">\n                                                <div class=\"card w-100\">\n                                                    <div class=\"card-header d-flex justify-content-between\">\n                                                        <p class=\"fw-bold mb-0\">Lucy</p>\n                                                        <p class=\"text-muted small mb-0\"><i class=\"far fa-clock\"></i> ".concat(moment().format("D MMM h:mm a"), "</p>\n                                                    </div>\n                                                    <div class=\"card-body\">\n                                                        <div class=\"canvas-imagine\" style=\"display: block; min-height: 250px;\">\n                                                        </div>\n                                                    </div>\n                                                </div>\n                                                <img src=\"/img/Lucy.png\" alt=\"avatar\"\n                                                     class=\"rounded-circle d-flex align-self-start ms-3 shadow-1-strong\" width=\"60\">\n                                            </li>");
                var canvases = document.getElementsByClassName('canvas-imagine');
                var canvas = canvases.item(canvases.length - 1);
                canvas.style.background = "url(".concat(response.data[0].url, ")");
                canvas.style.backgroundSize = 'contain';
                canvas.style.backgroundRepeat = 'no-repeat';
                canvas.style.backgroundPosition = 'center';
                var lastMsg = document.getElementsByClassName('direct-chat-msg');
                _this.diagnostic.scrollTo({ top: lastMsg.item(lastMsg.length - 1).offsetTop, behavior: 'smooth' });
            }
        }).fail(function (XMLHttpRequest, textStatus, errorThrown) {
            console.log("Message: " + JSON.stringify(XMLHttpRequest));
            console.log("Status: " + textStatus);
            console.log("Error: " + errorThrown);
        });
    };
    VoiceRecognizer.prototype.translate = function (prompt) {
        var _this = this;
        return $.ajax({
            type: 'POST',
            url: 'https://api.openai.com/v1/completions',
            contentType: "application/json",
            beforeSend: function (xhr) {
                xhr.setRequestHeader("Authorization", "Bearer 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");
            },
            data: JSON.stringify({
                model: "text-davinci-003",
                prompt: "Translate this into 1. French, 2. Spanish and 3. Japanese:\n\nWhat rooms do you have available?\n\n1.",
                temperature: 0.3,
                max_tokens: 100,
                top_p: 1.0,
                frequency_penalty: 0.0,
                presence_penalty: 0.0
            })
        }).then(function (response, textStatus, xhr) {
            if (xhr.status === 200) {
                console.log(JSON.stringify(response));
                var msg = response.choices[0].message.content;
                _this.conversation.push({ "role": "assistant", "content": msg });
                _this.diagnostic.innerHTML += "<li class=\"d-flex justify-content-between mb-2 direct-chat-msg pull-right\" dir=\"auto\">\n                                                <div class=\"card w-100\">\n                                                    <div class=\"card-header d-flex justify-content-between\">\n                                                        <p class=\"fw-bold mb-0\">Lucy</p>\n                                                        <p class=\"text-muted small mb-0\"><i class=\"far fa-clock\"></i> ".concat(moment().format("D MMM h:mm a"), "</p>\n                                                    </div>\n                                                    <div class=\"card-body\">\n                                                        <p class=\"mb-0\">\n                                                             ").concat(msg, "\n                                                        </p>\n                                                    </div>\n                                                </div>\n                                                <img src=\"/img/Lucy.png\" alt=\"avatar\"\n                                                     class=\"rounded-circle d-flex align-self-start ms-3 shadow-1-strong\" width=\"60\">\n                                            </li>");
                var lastMsg = document.getElementsByClassName('direct-chat-msg');
                _this.diagnostic.scrollTo({ top: lastMsg.item(lastMsg.length - 1).offsetTop, behavior: 'smooth' });
                _this.speak(msg);
            }
        }).fail(function (XMLHttpRequest, textStatus, errorThrown) {
            console.log("Message: " + JSON.stringify(XMLHttpRequest));
            console.log("Status: " + textStatus);
            console.log("Error: " + errorThrown);
        });
    };
    VoiceRecognizer.prototype.speak = function (msg) {
        var _this = this;
        var utterance = new SpeechSynthesisUtterance(msg);
        utterance.lang = this.language;
        utterance.voice = this.voice;
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
    };
    return VoiceRecognizer;
}());
var Equalizer = /** @class */ (function () {
    function Equalizer(profilePicture, stream) {
        //$(document).on('click', 'img', () => {
        var _this = this;
        this.profilePicture = profilePicture;
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
                resolve(navigator.mediaDevices.getUserMedia(constraints));
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